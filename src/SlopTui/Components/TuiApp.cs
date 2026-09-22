using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlopTui.Input;
using SlopTui.Layout;
using SlopTui.Rendering;
using SlopTui.Styling;
using SlopTui.Terminal;

namespace SlopTui.Components;

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

    public static readonly TuiAppOptions Default = new();
}

/// <summary>
/// Runs a root component in the terminal until <see cref="Exit"/> is called.
/// </summary>
/// <remarks>
/// The thread that calls <see cref="Run{TRoot}"/> is the Blazor dispatcher,
/// routes input and paints. Keys go to the focused element and bubble up
/// through its ancestors until a handler sets <c>Handled</c>. An exception
/// from a component ends the loop and is rethrown once the terminal is restored.
/// </remarks>
public sealed class TuiApp
{
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

    public TuiApp(ITerminal? terminal = null, TuiAppOptions? options = null, ILoggerFactory? loggerFactory = null)
    {
        _terminal = terminal ?? new ConsoleTerminal();
        _options = options ?? TuiAppOptions.Default;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        Services.AddSingleton(this);
        Services.AddSingleton(_terminal);
        Services.AddSingleton(Graphics);
    }

    /// <summary>The terminal's picture capabilities and the images sent to it.</summary>
    public Graphics Graphics { get; } = new();

    /// <summary>Services for components to <c>@inject</c>, registered before <see cref="Run{TRoot}"/>.</summary>
    public IServiceCollection Services { get; } = new ServiceCollection();

    /// <summary>The stylesheets in cascade order. Changing them restyles every element.</summary>
    public IList<Stylesheet> Stylesheets => _styles.Sheets;

    /// <summary>Parses CSS and adds it as the last stylesheet.</summary>
    public Stylesheet AddStylesheet(string css) => _styles.Sheets.Add(css);

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
        _dispatcher.BindToCurrentThread();
        if (ScopedStylesheets) LoadScopedStylesheets();
        var provider = Services.BuildServiceProvider();
        _renderer = new TerminalRenderer(provider, _loggerFactory, _dispatcher, OnException, _styles);
        _focus = new FocusManager(_renderer.Root, NotifyFocusAsync, work => _dispatcher.Post(work), _styles);
        _focus.Changed += (previous, current) => _renderer.FocusChanged(previous, current);
        _styles.Sheets.Changed += OnStylesheetsChanged;
        _size = _terminal.Size;
        _screen = new Screen(_size.Width, _size.Height) { SynchronizedOutput = SynchronizedOutput };

        _terminal.InputReceived += _pump.Enqueue;
        _terminal.Resized += OnResized;
        _pump.ChunkQueued += () => _dispatcher.Signal.Release();

        try
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
            _renderer.SetViewport(_size);
            var parameterView = parameters is null
                ? ParameterView.Empty
                : ParameterView.FromDictionary(new Dictionary<string, object?>(parameters));
            var render = _renderer.AddRootComponentAsync(rootComponent, parameterView);
            if (render.IsFaulted) throw render.Exception!.GetBaseException();
            Loop();
        }
        finally
        {
            _styles.Sheets.Changed -= OnStylesheetsChanged;
            var release = Graphics.ReleaseAll();
            if (release.Length > 0) _terminal.Write(release);
            _terminal.Stop();
            _terminal.InputReceived -= _pump.Enqueue;
            _terminal.Resized -= OnResized;
            _renderer.Dispose();
            provider.Dispose();
        }

        if (_failure is not null) throw new TuiAppException(_failure);
        return _exitCode;
    }

    private void OnStylesheetsChanged()
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

    private Task NotifyFocusAsync(HostElement element, bool gained) =>
        _renderer!.RaiseAsync(element, gained ? "onfocus" : "onblur", new FocusEventArgs(gained));

    private void Loop()
    {
        long? lastFrame = null;
        while (!_exitRequested)
        {
            _dispatcher.RunPending(OnException);
            RouteInput();
            _dispatcher.RunPending(OnException);
            if (_exitRequested) break;

            if (_resized)
            {
                _resized = false;
                _renderer!.SetViewport(_size);
                _screen!.Resize(_size.Width, _size.Height);
                _renderer.Dirty = true;
            }

            var due = lastFrame is not { } last || Stopwatch.GetElapsedTime(last) >= _options.FrameInterval;
            if (_renderer!.Dirty && due)
            {
                PaintFrame();
                lastFrame = Stopwatch.GetTimestamp();
            }

            _dispatcher.Signal.Wait(NextWait(lastFrame));
        }
    }

    private TimeSpan NextWait(long? lastFrame)
    {
        var wait = Timeout.InfiniteTimeSpan;
        if (_renderer!.Dirty)
        {
            var until = lastFrame is { } last ? _options.FrameInterval - Stopwatch.GetElapsedTime(last) : TimeSpan.Zero;
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
        var layoutStart = Stopwatch.GetTimestamp();
        FlexLayout.Layout(_renderer.Root.Node, _size);

        var paintStart = Stopwatch.GetTimestamp();
        _screen!.Back.Fill(Cell.Blank);
        Painter.Paint(_renderer.Root.Node, _screen.Back);
        Overlay?.Invoke(_screen.Back);
        PlaceCursor();

        var flushStart = Stopwatch.GetTimestamp();
        var frame = _screen.Flush();
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
        var args = new KeyPressEventArgs(key);
        await BubbleAsync(_focus!.Focused, "onkeypress", args, () => args.Handled);
        if (args.Handled) return;

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
            RepaintPictures();
        }
        else if (sequence.StartsWith("\e[?", StringComparison.Ordinal) && sequence.EndsWith('c'))
        {
            Graphics.Detected = true;
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

        if (mouse.Action == MouseAction.Pressed)
        {
            await BubbleAsync(target, "onclick", args, () => args.Handled);
            if (args.Handled) return;
        }
        await BubbleAsync(target, "onmouse", args, () => args.Handled);
    }

    private async Task FocusClosestFocusableAsync(HostElement target)
    {
        var focusable = target.Focusable ? target : target.AncestorElements().FirstOrDefault(a => a.Focusable);
        if (focusable is null || ReferenceEquals(focusable, _focus!.Focused)) return;

        await _focus.FocusAsync(focusable);
        _renderer!.Dirty = true;
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
