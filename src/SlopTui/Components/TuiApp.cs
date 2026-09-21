using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlopTui.Input;
using SlopTui.Layout;
using SlopTui.Rendering;
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
    private TerminalRenderer? _renderer;
    private FocusManager? _focus;
    private Screen? _screen;
    private Exception? _failure;
    private int _exitCode;
    private volatile bool _exitRequested;
    private volatile bool _resized;
    private Size _size;

    public TuiApp(ITerminal? terminal = null, TuiAppOptions? options = null, ILoggerFactory? loggerFactory = null)
    {
        _terminal = terminal ?? new ConsoleTerminal();
        _options = options ?? TuiAppOptions.Default;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        Services.AddSingleton(this);
        Services.AddSingleton(_terminal);
        Services.AddSingleton(new CanvasRegistry());
    }

    /// <summary>Services for components to <c>@inject</c>, registered before <see cref="Run{TRoot}"/>.</summary>
    public IServiceCollection Services { get; } = new ServiceCollection();

    public FocusManager Focus => _focus ?? throw new InvalidOperationException("The app is not running.");

    public TerminalRenderer Renderer => _renderer ?? throw new InvalidOperationException("The app is not running.");

    public Size Size => _size;

    /// <summary>Whether frames are wrapped in synchronized-output markers (DEC 2026).</summary>
    public bool SynchronizedOutput { get; set; } = true;

    /// <summary>Raised on the loop thread after every painted frame.</summary>
    public event Action<FrameStats>? FramePainted;

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
        var provider = Services.BuildServiceProvider();
        _renderer = new TerminalRenderer(provider, _loggerFactory, _dispatcher, OnException);
        _focus = new FocusManager(_renderer.Root, NotifyFocusAsync);
        _size = _terminal.Size;
        _screen = new Screen(_size.Width, _size.Height) { SynchronizedOutput = SynchronizedOutput };

        _terminal.InputReceived += _pump.Enqueue;
        _terminal.Resized += OnResized;
        _pump.ChunkQueued += () => _dispatcher.Signal.Release();

        try
        {
            _terminal.Start(_options.Terminal);
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
            _terminal.Stop();
            _terminal.InputReceived -= _pump.Enqueue;
            _terminal.Resized -= OnResized;
            _renderer.Dispose();
            provider.Dispose();
        }

        if (_failure is not null) throw new TuiAppException(_failure);
        return _exitCode;
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
        PlaceCursor();

        var flushStart = Stopwatch.GetTimestamp();
        var frame = _screen.Flush();
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
        if (owner?.Cursor is not { } caret || owner.Node.Layout.IsEmpty)
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
