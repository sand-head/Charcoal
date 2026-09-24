using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Charcoal.Input;
using Charcoal.Layout;
using Charcoal.Rendering;
using Charcoal.Routing;
using Charcoal.Styling;
using Charcoal.Terminal;

namespace Charcoal.Components;

/// <summary>Built-in behaviour of <see cref="TuiApp"/>.</summary>
public sealed record TuiAppOptions
{
    /// <summary>End the app on an unhandled Ctrl+C.</summary>
    public bool ExitOnCtrlC { get; init; } = true;

    /// <summary>Move focus on an unhandled Tab or Shift+Tab.</summary>
    public bool TabMovesFocus { get; init; } = true;

    /// <summary>The shortest interval between two frames.</summary>
    public TimeSpan FrameInterval { get; init; } = TimeSpan.FromMilliseconds(16);

    public TerminalOptions Terminal { get; init; } = TerminalOptions.Default;

    /// <summary>Whether dragging the mouse selects text; see <see cref="TuiApp.Selection"/>.</summary>
    public bool MouseSelection { get; init; } = true;

    /// <summary>
    /// The app's location for <c>@page</c> routing. By default a
    /// <see cref="TerminalNavigationManager"/> starting at <c>tui:///</c>.
    /// </summary>
    public TerminalNavigationManager? Navigation { get; init; }

    /// <summary>The rows one notch of the mouse wheel scrolls.</summary>
    public int WheelRows { get; init; } = 3;

    public static readonly TuiAppOptions Default = new();
}

/// <summary>
/// Runs a root component in the terminal until <see cref="Exit"/> is called.
/// </summary>
/// <remarks>
/// The thread that calls <see cref="Run{TRoot}"/> is the Blazor dispatcher,
/// routes input and paints. Keys go to the focused element's <c>@onkeydown</c>
/// and bubble up through its ancestors until a handler sets <c>Handled</c>. An
/// exception from a component ends the loop and is rethrown once the terminal
/// is restored.
/// </remarks>
public sealed class TuiApp
{
    private const int MaxContainerPasses = 3;

    private readonly TerminalDispatcher _dispatcher = new();
    private readonly InputPump _pump = new();
    private readonly ITerminal _terminal;
    private readonly TuiAppOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly StyleContext _styles = new();
    private TerminalRenderer? _renderer;
    private FocusManager? _focus;
    private Screen? _screen;
    private Exception? _failure;
    private int _exitCode;
    private volatile bool _exitRequested;
    private volatile bool _resized;
    private Size _size;
    private string _startup = "";
    private bool _sixelRepaint;
    private TerminalNavigationManager? _navigation;
    private long? _lastFrame;

    public TuiApp(ITerminal? terminal = null, TuiAppOptions? options = null, ILoggerFactory? loggerFactory = null)
    {
        _terminal = terminal ?? new ConsoleTerminal();
        _options = options ?? TuiAppOptions.Default;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        Services.AddSingleton(this);
        Services.AddSingleton(_terminal);
        Services.AddSingleton(Graphics);
        // Components, including Blazor's Router, can ask for loggers.
        Services.AddSingleton(_loggerFactory);
        Services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        // Unused unless the app has a Router.
        Services.AddSingleton(_ => _options.Navigation ?? new TerminalNavigationManager());
        Services.AddSingleton<NavigationManager>(provider => provider.GetRequiredService<TerminalNavigationManager>());
        Services.AddSingleton<INavigationInterception, TerminalNavigationManager.Interception>();
        Services.AddSingleton<IScrollToLocationHash, TerminalNavigationManager.NoHashScrolling>();
    }

    /// <summary>The terminal's picture capabilities and the images sent to it.</summary>
    public Graphics Graphics { get; } = new();

    /// <summary>The app's <see cref="NavigationManager"/>, for navigating from outside a component.</summary>
    public TerminalNavigationManager Navigation =>
        _navigation ?? throw new InvalidOperationException("The app is not running.");

    /// <summary>
    /// Raised when a link is activated, before it is followed. Links outside
    /// the app, such as <c>https:</c> or <c>mailto:</c>, are only reported here.
    /// </summary>
    public event Action<HostElement>? LinkFollowed;

    /// <summary>The text the mouse has selected. A release copies it to the clipboard.</summary>
    public MouseSelection Selection { get; } = new();

    /// <summary>Puts text on the terminal's clipboard with OSC 52. Safe to call from any thread.</summary>
    public void CopyToClipboard(string text)
    {
        var escape = Ansi.CopyToClipboard(text);
        if (escape.Length > 0) _terminal.Write(escape);
    }

    /// <summary>Services for components to <c>@inject</c>, registered before <see cref="Run{TRoot}"/>.</summary>
    public IServiceCollection Services { get; } = new ServiceCollection();

    /// <summary>The stylesheets in cascade order. Changing them restyles every element.</summary>
    public IList<Stylesheet> Stylesheets => _styles.Sheets;

    /// <summary>Parses CSS and adds it as the last stylesheet.</summary>
    public Stylesheet AddStylesheet(string css) => _styles.Sheets.Add(css);

    /// <summary>
    /// What <c>@media</c> queries see: the terminal's size, colour scheme and
    /// colour depth, whether the mouse is on, and a reduced-motion preference
    /// the app may set. Changing it restyles when a sheet uses <c>@media</c>.
    /// </summary>
    public MediaEnvironment Media
    {
        get => _styles.Media;
        set => SetMedia(value);
    }

    /// <summary>
    /// Whether to ask the terminal for its background colour at start and set
    /// <c>prefers-color-scheme</c> from its lightness. Until it answers, the
    /// scheme is dark.
    /// </summary>
    public bool DetectColorScheme { get; set; } = true;

    private void SetMedia(MediaEnvironment media)
    {
        if (ReferenceEquals(_styles.Media, media) || _styles.Media == media) return;
        _styles.Media = media;
        if (_styles.UsesMedia) RestyleAll();
    }

    /// <summary>Bits per colour component, from the environment of a real console.</summary>
    private int ColorBits()
    {
        if (_terminal is not ConsoleTerminal) return _styles.Media.ColorBits;
        var colorTerm = Environment.GetEnvironmentVariable("COLORTERM") ?? "";
        var trueColor = colorTerm.Contains("truecolor", StringComparison.OrdinalIgnoreCase)
            || colorTerm.Contains("24bit", StringComparison.OrdinalIgnoreCase);
        if (trueColor) return 8;
        var term = Environment.GetEnvironmentVariable("TERM") ?? "";
        return term.Contains("256", StringComparison.Ordinal) ? 4 : 2;
    }

    /// <summary>
    /// Whether to load, before the first frame, the component-scoped
    /// stylesheets the build embedded in the app's assemblies.
    /// </summary>
    public bool ScopedStylesheets { get; set; } = true;

    /// <summary>
    /// Adds the scoped stylesheet bundle the build embedded in an assembly
    /// from its <c>.razor.css</c> files. Returns null when there is none.
    /// </summary>
    public Stylesheet? AddScopedStylesheets(Assembly assembly)
    {
        var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(".bundle.scp.css", StringComparison.Ordinal));
        if (name is null) return null;

        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return _styles.Sheets.Add(reader.ReadToEnd());
    }

    /// <summary>
    /// Loads the scoped sheets of every non-framework assembly the app uses,
    /// dependencies first, so the app's own sheet wins ties.
    /// </summary>
    private void LoadScopedStylesheets()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<Assembly>();
        void Visit(Assembly assembly)
        {
            var name = assembly.GetName().Name ?? "";
            if (assembly.IsDynamic || IsFrameworkAssembly(name) || !seen.Add(name)) return;
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (TryLoad(reference) is { } loaded) Visit(loaded);
            }
            ordered.Add(assembly);
        }

        if (Assembly.GetEntryAssembly() is { } entry) Visit(entry);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Visit(assembly);
        }
        foreach (var assembly in ordered)
        {
            AddScopedStylesheets(assembly);
        }
    }

    private static bool IsFrameworkAssembly(string name) =>
        name.StartsWith("System", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.", StringComparison.Ordinal)
        || name is "mscorlib" or "netstandard" or "WindowsBase";

    private static Assembly? TryLoad(AssemblyName name)
    {
        try
        {
            return Assembly.Load(name);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            return null;
        }
    }

    public FocusManager Focus => _focus ?? throw new InvalidOperationException("The app is not running.");

    public TerminalRenderer Renderer => _renderer ?? throw new InvalidOperationException("The app is not running.");

    public Size Size => _size;

    /// <summary>Whether frames are wrapped in synchronized-output markers (DEC 2026).</summary>
    public bool SynchronizedOutput { get; set; } = true;

    /// <summary>
    /// Whether to query the terminal's graphics support and cell size at start.
    /// Without it, pictures are drawn as half blocks.
    /// </summary>
    public bool DetectGraphics { get; set; } = true;

    /// <summary>Raised on the loop thread after every painted frame.</summary>
    public event Action<FrameStats>? FramePainted;

    /// <summary>
    /// Raised after the tree is painted and before the frame is flushed, to
    /// draw over everything else.
    /// </summary>
    public event Action<CellBuffer>? Overlay;

    /// <summary>The terminal, for raw writes.</summary>
    public ITerminal Terminal => _terminal;

    /// <summary>Timings of the last painted frame.</summary>
    public FrameStats LastFrame { get; private set; }

    public long FrameCount { get; private set; }

    /// <summary>Raised on the loop thread for every input event, before routing.</summary>
    public event Action<InputEvent>? Input;

    /// <summary>Raised with any exception from a handler or component; return true to keep running.</summary>
    public event Func<Exception, bool>? Error;

    /// <summary>Stops the loop after the current iteration. Safe to call from any thread.</summary>
    public void Exit(int code = 0)
    {
        _exitCode = code;
        _exitRequested = true;
        _dispatcher.Signal.Release();
    }

    /// <summary>Runs work on the loop thread.</summary>
    public Task InvokeAsync(Action work) => _dispatcher.InvokeAsync(work);

    /// <summary>Runs work on the loop thread.</summary>
    public Task InvokeAsync(Func<Task> work) => _dispatcher.InvokeAsync(work);

    /// <summary>Requests a frame even though no component re-rendered.</summary>
    public void Invalidate()
    {
        if (_renderer is not null) _renderer.Dirty = true;
        _dispatcher.Signal.Release();
    }

    /// <summary>Runs the root component until <see cref="Exit"/> and returns the exit code.</summary>
    public int Run<TRoot>(IReadOnlyDictionary<string, object?>? parameters = null) where TRoot : IComponent =>
        Run(typeof(TRoot), parameters);

    /// <inheritdoc cref="Run{TRoot}"/>
    public int Run(Type rootComponent, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        var provider = Prepare();
        try
        {
            Launch(rootComponent, parameters);
            while (Step())
            {
                _dispatcher.Signal.Wait(NextWait());
            }
        }
        finally
        {
            TearDown(provider);
        }
        return ExitCode();
    }

    /// <summary>
    /// Runs the root component until <see cref="Exit"/>, awaiting input
    /// instead of blocking.
    /// </summary>
    /// <remarks>
    /// For a host with one thread that must not block, such as WebAssembly in
    /// a browser. The loop must stay on the thread that started it, so
    /// anywhere else use <see cref="Run{TRoot}"/>.
    /// </remarks>
    public Task<int> RunAsync<TRoot>(IReadOnlyDictionary<string, object?>? parameters = null) where TRoot : IComponent =>
        RunAsync(typeof(TRoot), parameters);

    /// <inheritdoc cref="RunAsync{TRoot}"/>
    public async Task<int> RunAsync(Type rootComponent, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        var provider = Prepare();
        try
        {
            Launch(rootComponent, parameters);
            while (Step())
            {
                await _dispatcher.Signal.WaitAsync(NextWait());
            }
        }
        finally
        {
            TearDown(provider);
        }
        return ExitCode();
    }

    /// <summary>Builds the services, the renderer and focus, and subscribes to the terminal.</summary>
    private ServiceProvider Prepare()
    {
        _dispatcher.BindToCurrentThread();
        if (ScopedStylesheets) LoadScopedStylesheets();
        var provider = Services.BuildServiceProvider();
        _navigation = provider.GetRequiredService<TerminalNavigationManager>();
        // Navigation from outside a component re-renders without marking the tree dirty.
        _navigation.LocationChanged += (_, _) => Invalidate();
        _renderer = new TerminalRenderer(provider, _loggerFactory, _dispatcher, OnException, _styles);
        _focus = new FocusManager(_renderer.Root, NotifyFocusAsync, work => _dispatcher.Post(work), _styles);
        _focus.Changed += (previous, current) => _renderer.FocusChanged(previous, current);
        _renderer.AutofocusRequested += OnAutofocus;
        // A render on the loop thread outside a step, such as a timer's on a
        // single-threaded host, is not queued work, so it wakes the loop itself.
        _renderer.BatchApplied += () => _dispatcher.Signal.Release();
        _styles.Sheets.Changed += OnStylesheetsChanged;
        _size = _terminal.Size;
        _screen = new Screen(_size.Width, _size.Height) { SynchronizedOutput = SynchronizedOutput };

        _terminal.InputReceived += _pump.Enqueue;
        _terminal.Resized += OnResized;
        _pump.ChunkQueued += () => _dispatcher.Signal.Release();
        return provider;
    }

    /// <summary>Starts the terminal, queues the startup queries and renders the root component.</summary>
    private void Launch(Type rootComponent, IReadOnlyDictionary<string, object?>? parameters)
    {
        _terminal.Start(_options.Terminal);
        // Sent with the first frame. DA1 comes last because every terminal
        // answers it, so its reply means the others will not come.
        if (DetectGraphics && _terminal.IsInteractive)
        {
            _startup = KittyGraphics.Query + Ansi.QueryCellPixels + Ansi.QueryDeviceAttributes;
        }
        else
        {
            Graphics.Detected = true;
        }
        if (DetectColorScheme && _terminal.IsInteractive) _startup = Ansi.QueryBackground + _startup;
        _styles.Media = _styles.Media with
        {
            Width = _size.Width,
            Height = _size.Height,
            ColorBits = ColorBits(),
            Pointer = _options.Terminal.Mouse,
        };
        _renderer!.SetViewport(_size);
        var parameterView = parameters is null
            ? ParameterView.Empty
            : ParameterView.FromDictionary(new Dictionary<string, object?>(parameters));
        var render = _renderer.AddRootComponentAsync(rootComponent, parameterView);
        if (render.IsFaulted) throw render.Exception!.GetBaseException();
    }

    private void TearDown(ServiceProvider provider)
    {
        _styles.Sheets.Changed -= OnStylesheetsChanged;
        var release = Graphics.ReleaseAll();
        if (release.Length > 0) _terminal.Write(release);
        _terminal.Stop();
        _terminal.InputReceived -= _pump.Enqueue;
        _terminal.Resized -= OnResized;
        _renderer?.Dispose();
        provider.Dispose();
    }

    private int ExitCode()
    {
        if (_failure is not null) throw new TuiAppException(_failure);
        return _exitCode;
    }

    private void OnStylesheetsChanged() => RestyleAll();

    private void RestyleAll()
    {
        if (_renderer is null) return;
        if (_dispatcher.CheckAccess())
        {
            _renderer.RestyleAll();
        }
        else
        {
            _ = _dispatcher.InvokeAsync(_renderer.RestyleAll);
        }
        _dispatcher.Signal.Release();
    }

    private void OnResized(Size size)
    {
        _size = size;
        _resized = true;
        _dispatcher.Signal.Release();
    }

    private void OnException(Exception exception)
    {
        if (Error?.Invoke(exception) == true) return;
        _failure ??= exception;
        Exit(1);
    }

    private async Task NotifyFocusAsync(HostElement element, bool gained)
    {
        // Leaving a field commits its value before the blur.
        if (!gained)
        {
            await CommitAsync(element);
        }
        await _renderer!.RaiseAsync(element, gained ? "onfocus" : "onblur", new FocusEventArgs());
    }

    /// <summary>
    /// Focuses an element with <c>autofocus</c> when nothing else has focus,
    /// including when the focused element was removed in the same batch.
    /// </summary>
    private void OnAutofocus(HostElement element)
    {
        _focus!.DropIfDetached();
        if (_focus.Focused is null)
        {
            _ = _focus.FocusAsync(element);
        }
    }

    /// <summary>Raises <c>onchange</c> if the field's value changed since it was last committed.</summary>
    private async Task CommitAsync(HostElement element)
    {
        if (element.Control is not { } control || !control.TakeChange(out var value)) return;
        await _renderer!.RaiseAsync(element, "onchange", new ChangeEventArgs { Value = value }, value);
    }

    private Task InputAsync(HostElement element)
    {
        var value = element.Control!.Value;
        return _renderer!.RaiseAsync(element, "oninput", new ChangeEventArgs { Value = value }, value);
    }

    /// <summary>
    /// One turn of the loop: queued work, input, a resize and a frame when one
    /// is due. Returns false once the app should stop.
    /// </summary>
    private bool Step()
    {
        if (_exitRequested) return false;
        _dispatcher.RunPending(OnException);
        RouteInput();
        _dispatcher.RunPending(OnException);
        if (_exitRequested) return false;

        if (_resized)
        {
            _resized = false;
            SetMedia(_styles.Media with { Width = _size.Width, Height = _size.Height });
            _renderer!.SetViewport(_size);
            _screen!.Resize(_size.Width, _size.Height);
            _sixelRepaint = true;
            _renderer.Dirty = true;
        }

        var due = _lastFrame is not { } last || Stopwatch.GetElapsedTime(last) >= _options.FrameInterval;
        if (_renderer!.Dirty && due)
        {
            PaintFrame();
            _lastFrame = Stopwatch.GetTimestamp();
        }
        return true;
    }

    private TimeSpan NextWait()
    {
        var wait = Timeout.InfiniteTimeSpan;
        if (_renderer!.Dirty)
        {
            var until = _lastFrame is { } last ? _options.FrameInterval - Stopwatch.GetElapsedTime(last) : TimeSpan.Zero;
            wait = until <= TimeSpan.Zero ? TimeSpan.Zero : until;
        }
        if (_pump.PendingEscapeDeadlineTicks is { } deadline)
        {
            var untilEsc = TimeSpan.FromTicks(Math.Max(0, deadline - DateTime.UtcNow.Ticks));
            if (wait == Timeout.InfiniteTimeSpan || untilEsc < wait) wait = untilEsc;
        }
        return wait;
    }

    private void PaintFrame()
    {
        _renderer!.Dirty = false;
        _focus!.DropIfDetached();
        // A field disabled while focused loses focus, with a blur.
        if (_focus.Focused is { Control.Disabled: true })
        {
            _ = _focus.FocusAsync(null);
        }
        var layoutStart = Stopwatch.GetTimestamp();
        ReanchorScrolledBoxes();
        FlexLayout.Layout(_renderer.Root.Node, _size);
        if (_styles.UsesContainer)
        {
            ContainerPass();
        }
        // Scrolling needs the sizes from the layout, and moving an offset
        // needs another arrangement before the paint.
        if (ScrollPass())
        {
            FlexLayout.Layout(_renderer.Root.Node, _size);
        }
        RecordAnchors();

        var paintStart = Stopwatch.GetTimestamp();
        _screen!.Back.Fill(Cell.Blank);
        Graphics.BeginFrame();
        Painter.Paint(_renderer.Root.Node, _screen.Back);
        Selection.Highlight(_screen.Back);
        Overlay?.Invoke(_screen.Back);
        PlaceCursor();

        var flushStart = Stopwatch.GetTimestamp();
        var resendSixels = _sixelRepaint;
        _sixelRepaint = false;
        var frame = _screen.Flush(rows => Graphics.SixelOutput(rows, resendSixels));
        // Images go ahead of the frame that shows them, in the same write.
        if (Graphics.HasPending) frame = Graphics.TakePending() + frame;
        if (_startup.Length > 0)
        {
            frame = _startup + frame;
            _startup = "";
        }
        if (frame.Length > 0) _terminal.Write(frame);
        var flushEnd = Stopwatch.GetTimestamp();

        FrameCount++;
        LastFrame = new FrameStats(
            Layout: Stopwatch.GetElapsedTime(layoutStart, paintStart),
            Paint: Stopwatch.GetElapsedTime(paintStart, flushStart),
            Flush: Stopwatch.GetElapsedTime(flushStart, flushEnd),
            Bytes: frame.Length);
        FramePainted?.Invoke(LastFrame);
    }

    /// <summary>
    /// Restyles the descendants of every query container whose size changed in
    /// the last layout, and lays out again when any did. A container's size does
    /// not depend on its contents, so this settles quickly; the cap stops rules
    /// that fight each other.
    /// </summary>
    private void ContainerPass()
    {
        for (var pass = 0; pass < MaxContainerPasses; pass++)
        {
            var changed = false;
            foreach (var node in _renderer!.Root.Descendants())
            {
                if (node is HostElement { Node.Style.ContainerType: not ContainerType.Normal } container && UpdateContainer(container))
                {
                    changed = true;
                }
            }
            if (!changed) return;
            FlexLayout.Layout(_renderer.Root.Node, _size);
        }
    }

    private static bool UpdateContainer(HostElement container)
    {
        var box = container.Node.Layout.Deflate(container.Node.Style.Inset);
        var size = new Size(Math.Max(0, box.Width), Math.Max(0, box.Height));
        if (!container.SetContainerSize(size)) return false;
        container.RestyleDescendants();
        return true;
    }

    private void PlaceCursor()
    {
        // Only the focused element's caret is shown.
        var owner = _focus!.Focused;
        if (owner?.Caret is not { } caret || owner.Node.Layout.IsEmpty)
        {
            _screen!.CursorVisible = false;
            _screen.Cursor = null;
            return;
        }
        var content = owner.Node.Layout.Deflate(owner.Node.Style.Inset);
        var (column, row) = caret;
        _screen!.Cursor = (content.X + column, content.Y + row);
        _screen.CursorVisible = true;
    }

    private void RouteInput()
    {
        var events = _pump.Drain(DateTime.UtcNow.Ticks);
        foreach (var inputEvent in events)
        {
            try
            {
                Input?.Invoke(inputEvent);
                var task = inputEvent switch
                {
                    KeyEvent key => RouteKeyAsync(key),
                    PasteEvent paste => RoutePasteAsync(paste),
                    MouseEvent mouse => RouteMouseAsync(mouse),
                    ReplyEvent reply => OnReply(reply),
                    _ => Task.CompletedTask,
                };
                // A handler that goes async reports its failure through the dispatcher.
                if (task.IsFaulted) OnException(task.Exception!.GetBaseException());
            }
            catch (Exception ex)
            {
                OnException(ex);
            }
        }
    }

    private async Task RouteKeyAsync(KeyEvent key)
    {
        var args = new KeyboardEventArgs(key);
        await BubbleAsync(_focus!.Focused, "onkeydown", args, () => args.Handled);
        if (args.Handled) return;

        // Fields edit with the keys the handlers left. Enter in an input
        // commits its value first.
        if (_focus.Focused is { Control: { } control } field)
        {
            if (key.Key == Key.Enter && !control.Multiline && !key.Ctrl && !key.Alt)
            {
                await CommitAsync(field);
            }
            if (control.HandleKey(key, out var edited))
            {
                _renderer!.Dirty = true;
                if (edited) await InputAsync(field);
                return;
            }
        }

        if (key.Key == Key.Enter && !key.Ctrl && !key.Alt && _focus.Focused is { IsLink: true } link)
        {
            Follow(link);
            return;
        }

        // Scroll keys go to the nearest scrollable ancestor of the focus. Fields
        // have already taken their own keys above.
        if (ScrollableFrom(_focus.Focused) is { } scroller && ScrollTargetFor(scroller, key) is { } scrollTop)
        {
            scroller.ScrollTop = scrollTop;
            _renderer!.Dirty = true;
            return;
        }

        // Ctrl+C copies while something is selected, and exits otherwise.
        if (key.IsCtrl('c') && Selection.Active)
        {
            CopySelection();
            return;
        }

        if (_options.TabMovesFocus && key.Key == Key.Tab && !key.Ctrl && !key.Alt)
        {
            if (key.Shift)
            {
                await _focus.PreviousAsync();
            }
            else
            {
                await _focus.NextAsync();
            }
            _renderer!.Dirty = true;
            return;
        }
        if (_options.ExitOnCtrlC && key.IsCtrl('c')) Exit();
    }

    /// <summary>
    /// The scroll offset a key moves a scroll container to, or null when the
    /// key does not scroll.
    /// </summary>
    private static int? ScrollTargetFor(HostElement box, KeyEvent key)
    {
        if (key.Ctrl || key.Alt) return null;

        var page = Math.Max(1, box.ClientHeight - 1);
        return key.Key switch
        {
            Key.Up => box.ScrollTop - 1,
            Key.Down => box.ScrollTop + 1,
            Key.PageUp => box.ScrollTop - page,
            Key.PageDown => box.ScrollTop + page,
            Key.Home => 0,
            Key.End => box.ScrollTopMax,
            _ => null,
        };
    }

    /// <summary>The nearest scroll container at or above an element that has room to scroll.</summary>
    private static HostElement? ScrollableFrom(HostElement? from)
    {
        for (var element = from; element is not null; element = element.AncestorElements().FirstOrDefault())
        {
            if (element.IsScrollContainer && element.ScrollTopMax > 0) return element;
        }
        return null;
    }

    /// <summary>
    /// Applies scroll anchoring after a layout, clamps every scroll container's
    /// offset to its content, and raises <c>scroll</c> on each box whose offset
    /// changed since the last report.
    /// </summary>
    /// <returns>Whether any offset changed, which requires another layout.</returns>
    private bool ScrollPass()
    {
        var moved = false;
        var scrolled = new List<HostElement>();
        foreach (var box in _renderer!.Root.Descendants().OfType<HostElement>().Where(e => e.IsScrollContainer))
        {
            moved |= KeepAnchorInPlace(box);
            moved |= ClampScroll(box);
            if (box.ReportedScrollTop != box.ScrollTop)
            {
                box.ReportedScrollTop = box.ScrollTop;
                scrolled.Add(box);
            }
        }

        // Handlers run after the frame, so one that re-renders does not
        // re-enter the batch being painted.
        foreach (var box in scrolled)
        {
            _ = _dispatcher.Post(() => _renderer.RaiseAsync(box, "onscroll", new ScrollEventArgs(box)));
        }
        return moved;
    }

    private static HostElement? NearestLink(HostElement from) =>
        from.IsLink ? from : from.AncestorElements().FirstOrDefault(e => e.IsLink);

    /// <summary>
    /// Navigates to a link's <c>href</c> if the app's router can handle it,
    /// as <c>blazor.web.js</c> does with an intercepted click. Other links are
    /// left to <see cref="LinkFollowed"/> handlers rather than opening a browser.
    /// </summary>
    private void Follow(HostElement link)
    {
        var href = link.Href!;
        LinkFollowed?.Invoke(link);
        if (_navigation is null || !IsInternal(href)) return;
        _navigation.NavigateTo(href);
    }

    /// <summary>Whether an href is a relative reference or an absolute URI under the app's base.</summary>
    private bool IsInternal(string href)
    {
        if (href.Length == 0) return false;
        return !HasScheme(href) || href.StartsWith(_navigation!.BaseUri, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a URI reference starts with a scheme, per RFC 3986.
    /// <c>Uri.TryCreate</c> cannot tell, because on Unix it reads <c>/settings</c>
    /// as <c>file:///settings</c>.
    /// </summary>
    private static bool HasScheme(string href)
    {
        var colon = href.IndexOf(':');
        if (colon <= 0 || !char.IsAsciiLetter(href[0])) return false;
        for (var i = 1; i < colon; i++)
        {
            var c = href[i];
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('+' or '-' or '.')) return false;
        }
        return true;
    }

    /// <summary>
    /// Scrolls by however far the anchor node moved in the layout, so content
    /// growing above what is in view does not push it down.
    /// </summary>
    private static bool KeepAnchorInPlace(HostElement box)
    {
        if (box.Node.Style.OverflowAnchor != OverflowAnchor.Auto) return false;
        if (box.AnchorNode is not { } anchor || !anchor.AncestorElements().Contains(box)) return false;
        // A scroll since the anchor was recorded was deliberate and stays.
        if (box.Node.ScrollTop != box.AnchorScrollTop) return false;

        var offset = anchor.Node.Layout.Y - box.Scrollport.Y;
        if (offset == box.AnchorOffset) return false;
        box.Node.ScrollTop = Math.Max(0, box.Node.ScrollTop + offset - box.AnchorOffset);
        return true;
    }

    private static bool ClampScroll(HostElement box)
    {
        var max = box.ScrollTopMax;
        if (box.ScrollTop <= max) return false;
        box.Node.ScrollTop = max;
        return true;
    }

    /// <summary>Chooses each scroll container's anchor node from the final layout of the frame.</summary>
    private void RecordAnchors()
    {
        foreach (var box in _renderer!.Root.Descendants().OfType<HostElement>().Where(e => e.IsScrollContainer))
        {
            RecordAnchor(box, 0);
        }
    }

    /// <summary>
    /// Chooses the anchor again for each box scrolled since its anchor was
    /// recorded, from the last layout moved by the scroll, as a browser does
    /// before the next layout. Content that grows in the same frame as the
    /// scroll is then corrected for, and the scroll itself is kept.
    /// </summary>
    private void ReanchorScrolledBoxes()
    {
        foreach (var box in _renderer!.Root.Descendants().OfType<HostElement>().Where(e => e.IsScrollContainer))
        {
            var scrolled = box.ScrollTop - box.AnchorScrollTop;
            if (scrolled != 0) RecordAnchor(box, scrolled);
        }
    }

    /// <summary>
    /// Records a box's anchor node and how far below the scrollport's top it
    /// sits, with the content moved up by <paramref name="scrolled"/> rows
    /// the last layout has not applied yet.
    /// </summary>
    private static void RecordAnchor(HostElement box, int scrolled)
    {
        if (box.Node.Style.OverflowAnchor == OverflowAnchor.None)
        {
            box.AnchorNode = null;
            return;
        }

        var scrollport = box.Scrollport.Offset(0, scrolled);
        box.AnchorNode = SelectAnchor(box, scrollport);
        box.AnchorOffset = box.AnchorNode is { } anchor ? anchor.Node.Layout.Y - scrollport.Y : 0;
        box.AnchorScrollTop = box.ScrollTop;
    }

    /// <summary>
    /// Selects the anchor node as CSS Scroll Anchoring does: the first visible
    /// element in tree order, preferring the deepest candidate and a fully
    /// visible one over a partly visible one. <c>overflow-anchor: none</c>
    /// excludes an element and its descendants, and nested scroll containers
    /// are not searched because they anchor themselves.
    /// </summary>
    private static HostElement? SelectAnchor(HostElement parent, Rect scrollport)
    {
        HostElement? partlyVisible = null;
        foreach (var child in parent.ChildElements())
        {
            var style = child.Node.Style;
            if (style.OverflowAnchor == OverflowAnchor.None || style.Display == Display.None) continue;

            var rect = child.Node.Layout;
            if (!IsInView(rect, scrollport)) continue;

            var candidate = (child.IsScrollContainer ? null : SelectAnchor(child, scrollport)) ?? child;
            if (IsFullyInView(rect, scrollport)) return candidate;
            partlyVisible ??= candidate;
        }
        return partlyVisible;
    }

    // An element without height holds the line it sits on. The web's
    // sentinel is 1px tall, but here the smallest height is a whole row.
    private static bool IsInView(Rect rect, Rect scrollport) => rect.Height <= 0
        ? rect.Y >= scrollport.Y && rect.Y <= scrollport.Bottom
        : rect.Bottom > scrollport.Y && rect.Y < scrollport.Bottom;

    private static bool IsFullyInView(Rect rect, Rect scrollport) =>
        rect.Height <= 0 || (rect.Y >= scrollport.Y && rect.Bottom <= scrollport.Bottom);

    /// <summary>
    /// Handles the answers to the start-up queries, repainting pictures that
    /// were drawn before the terminal said what it supports.
    /// </summary>
    private Task OnReply(ReplyEvent reply)
    {
        var sequence = reply.Sequence;
        if (KittyGraphics.IsQueryReply(sequence, out var ok))
        {
            Graphics.Kitty = ok;
            RepaintPictures();
        }
        else if (TryParseCellPixels(sequence, out var cellPixels))
        {
            Graphics.CellPixels = cellPixels;
            SetMedia(_styles.Media with { CellPixelWidth = cellPixels.Width, CellPixelHeight = cellPixels.Height });
            RepaintPictures();
        }
        else if (sequence.StartsWith("\e[?", StringComparison.Ordinal) && sequence.EndsWith('c'))
        {
            // Primary device attributes; attribute 4 is Sixel graphics.
            var attributes = sequence[3..^1].Split(';');
            if (attributes.Contains("4") && !Graphics.Sixel)
            {
                Graphics.Sixel = true;
                _sixelRepaint = true;
                RepaintPictures();
            }
            Graphics.Detected = true;
        }
        else if (sequence.StartsWith("\e]11;", StringComparison.Ordinal) && TryParseOscColor(sequence[5..], out var background))
        {
            var (r, g, b) = background;
            var luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            SetMedia(_styles.Media with { ColorScheme = luminance < 0.5 ? ColorScheme.Dark : ColorScheme.Light });
        }
        return Task.CompletedTask;
    }

    /// <summary>Reads the <c>CSI 6 ; height ; width t</c> reply to the cell size query.</summary>
    private static bool TryParseCellPixels(string sequence, out Size size)
    {
        size = Size.Empty;
        if (!sequence.StartsWith("\e[6;", StringComparison.Ordinal) || !sequence.EndsWith('t')) return false;

        var parts = sequence[2..^1].Split(';');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[1], out var height) || !int.TryParse(parts[2], out var width)) return false;
        if (width <= 0 || height <= 0) return false;

        size = new Size(width, height);
        return true;
    }

    /// <summary>
    /// Reads an OSC colour answer, <c>rgb:rrrr/gggg/bbbb</c> with one to four
    /// hex digits a channel or <c>#rrggbb</c>, as fractions of full intensity.
    /// </summary>
    internal static bool TryParseOscColor(string text, out (double R, double G, double B) color)
    {
        color = default;
        var value = text.TrimEnd('\a', '\e', '\\');
        if (value.StartsWith("rgb:", StringComparison.OrdinalIgnoreCase))
        {
            var channels = value[4..].Split('/');
            if (channels.Length != 3) return false;
            if (!TryParseHexChannel(channels[0], out var r) || !TryParseHexChannel(channels[1], out var g) || !TryParseHexChannel(channels[2], out var b))
            {
                return false;
            }
            color = (r, g, b);
            return true;
        }
        if (value.StartsWith('#') && Color.TryParse(value, out var parsed) && parsed.Kind == ColorKind.Rgb)
        {
            color = (parsed.R / 255.0, parsed.G / 255.0, parsed.B / 255.0);
            return true;
        }
        return false;
    }

    /// <summary>A channel of one to four hex digits, as a fraction of its maximum.</summary>
    private static bool TryParseHexChannel(string digits, out double fraction)
    {
        fraction = 0;
        if (digits.Length is < 1 or > 4) return false;
        if (!int.TryParse(digits, NumberStyles.HexNumber, null, out var value)) return false;

        var maximum = (1 << (4 * digits.Length)) - 1;
        fraction = value / (double)maximum;
        return true;
    }

    private void RepaintPictures()
    {
        if (_renderer is null) return;
        foreach (var element in _renderer.Root.Descendants().OfType<HostElement>().Where(e => e.IsImage))
        {
            element.Node.InvalidateLayout();
        }
        _renderer.Dirty = true;
    }

    private async Task RoutePasteAsync(PasteEvent paste)
    {
        var args = new PasteEventArgs(paste.Text);
        await BubbleAsync(_focus!.Focused, "onpaste", args, () => args.Handled);
        if (args.Handled) return;
        if (_focus.Focused is { Control: { } control } field && control.Paste(paste.Text))
        {
            _renderer!.Dirty = true;
            await InputAsync(field);
        }
    }

    private async Task RouteMouseAsync(MouseEvent mouse)
    {
        var target = _renderer!.Root.HitTest(mouse.X, mouse.Y);
        if (target is null) return;
        var args = new MouseEventArgs(mouse, target);
        if (mouse.Action == MouseAction.Pressed && mouse.Button == MouseButton.Left)
        {
            await FocusClosestFocusableAsync(target);
        }

        // A press raises click, then mousedown if click was not handled.
        if (mouse.Action is MouseAction.Pressed)
        {
            await BubbleAsync(target, "onclick", args, () => args.Handled);
            if (!args.Handled) await BubbleAsync(target, "onmousedown", args, () => args.Handled);
        }
        else if (mouse.Action is MouseAction.WheelUp or MouseAction.WheelDown)
        {
            await RouteWheelAsync(mouse, target);
            return;
        }
        else
        {
            var name = mouse.Action is MouseAction.Released ? "onmouseup" : "onmousemove";
            await BubbleAsync(target, name, args, () => args.Handled);
        }
        if (args.Handled) return;

        var leftPress = mouse.Action == MouseAction.Pressed && mouse.Button == MouseButton.Left;
        // Links often wrap their text in other inline elements.
        if (leftPress && NearestLink(target) is { } link)
        {
            Follow(link);
            return;
        }
        if (leftPress && target.Control is { } control && control.Click(mouse.X, mouse.Y))
        {
            _renderer.Dirty = true;
        }
        Select(mouse, target);
    }

    private async Task RouteWheelAsync(MouseEvent mouse, HostElement target)
    {
        var wheel = new WheelEventArgs(mouse, target);
        await BubbleAsync(target, "onwheel", wheel, () => wheel.Handled);
        if (!wheel.Handled && ScrollableFrom(target) is { } scroller)
        {
            scroller.ScrollTop += wheel.DeltaY * Math.Max(1, _options.WheelRows);
            _renderer!.Dirty = true;
        }
    }

    private async Task FocusClosestFocusableAsync(HostElement target)
    {
        var focusable = target.Focusable ? target : target.AncestorElements().FirstOrDefault(a => a.Focusable);
        if (focusable is null || ReferenceEquals(focusable, _focus!.Focused)) return;

        await _focus.FocusAsync(focusable);
        _renderer!.Dirty = true;
    }

    /// <summary>Updates the mouse selection with an event no handler took.</summary>
    private void Select(MouseEvent mouse, HostElement target)
    {
        if (!_options.MouseSelection || !Selection.Enabled) return;

        var at = new CellPosition(mouse.Y, mouse.X);
        switch (mouse.Action)
        {
            case MouseAction.Pressed when mouse.Button == MouseButton.Left:
                StartSelection(at, target);
                break;
            case MouseAction.Moved when Selection.Dragging:
                Selection.Drag(at);
                _renderer!.Dirty = true;
                break;
            case MouseAction.Released when Selection.Dragging:
                Selection.Release(at);
                if (Selection.CopyOnRelease)
                {
                    CopySelection();
                }
                else
                {
                    _renderer!.Dirty = true;
                }
                break;
        }
    }

    private void StartSelection(CellPosition at, HostElement target)
    {
        // Every press clears the old selection, even where nothing can be
        // selected, so a drag starting outside cannot extend it.
        var hadSelection = Selection.Active;
        Selection.Clear();
        var screen = new Rect(0, 0, _size.Width, _size.Height);
        if (MouseSelection.Allows(target, screen, out var region))
        {
            Selection.Press(at, region);
        }
        if (hadSelection)
        {
            _renderer!.Dirty = true;
        }
    }

    private void CopySelection()
    {
        var text = _screen is null ? null : Selection.Text(_screen.Back);
        Selection.Clear();
        _renderer!.Dirty = true;
        if (text is null) return;

        CopyToClipboard(text);
        Selection.RaiseCopied(text);
    }

    /// <summary>Raises the event on the element and then its ancestors until one handles it.</summary>
    private async Task BubbleAsync(HostElement? start, string eventName, EventArgs args, Func<bool> handled)
    {
        var root = _renderer!.Root;
        if (start is not null)
        {
            await _renderer.RaiseAsync(start, eventName, args);
            if (handled()) return;
            foreach (var ancestor in start.AncestorElements())
            {
                await _renderer.RaiseAsync(ancestor, eventName, args);
                if (handled()) return;
            }
        }
        else
        {
            // With nothing focused, the outermost elements get the event.
            foreach (var element in root.Children.SelectMany(TopElements))
            {
                await _renderer.RaiseAsync(element, eventName, args);
                if (handled()) return;
            }
        }
    }

    private static IEnumerable<HostElement> TopElements(HostNode node) => node switch
    {
        HostElement element => [element],
        HostContainer container => container.Children.SelectMany(TopElements),
        _ => [],
    };
}

/// <summary>How long each stage of a frame took, and how many characters it wrote.</summary>
public readonly record struct FrameStats(TimeSpan Layout, TimeSpan Paint, TimeSpan Flush, int Bytes)
{
    public TimeSpan Total => Layout + Paint + Flush;
}

/// <summary>Wraps an exception from a component or handler, thrown once the terminal is restored.</summary>
public sealed class TuiAppException : Exception
{
    public TuiAppException(Exception inner) : base(inner.Message, inner) { }
}
