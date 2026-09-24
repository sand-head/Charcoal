using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Charcoal.Components;
using Charcoal.Input;
using Charcoal.Layout;
using Charcoal.Terminal;

namespace Charcoal.Tests.Components;

public class TuiAppTests
{
    /// <summary>A component with a counter and a key handler, built without Razor.</summary>
    private sealed class Counter : ComponentBase
    {
        public static Counter? Last;
        public int Count;
        public bool Focused;
        public List<string> Keys = [];

        protected override void OnInitialized() => Last = this;

        public void Increment()
        {
            Count++;
            StateHasChanged();
        }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "tabindex", 0);
            b.AddAttribute(2, "style", "height: 100%");
            b.AddAttribute(3, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, e =>
            {
                Keys.Add(e.Key.ToString());
                if (e.Key.Text == "+") { Count++; e.Handled = true; }
            }));
            b.AddAttribute(4, "onfocus", EventCallback.Factory.Create<FocusEventArgs>(this, _ => Focused = true));
            b.AddAttribute(5, "onblur", EventCallback.Factory.Create<FocusEventArgs>(this, _ => Focused = false));
            b.OpenElement(6, "div");
            b.AddContent(7, $"count {Count}");
            b.CloseElement();
            b.OpenElement(8, "div");
            b.AddAttribute(9, "tabindex", 0);
            b.AddContent(10, "second");
            b.CloseElement();
            b.CloseElement();
        }
    }

    private static (TuiApp App, HeadlessTerminal Terminal, Task<int> Run) Start()
    {
        var terminal = new HeadlessTerminal(40, 10);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        var run = Task.Run(() => app.Run<Counter>());
        WaitUntil(() => terminal.Writes.Count > 0, "the first frame", run);
        return (app, terminal, run);
    }

    private static void WaitUntil(Func<bool> condition, string what, Task? run = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (run is { IsFaulted: true }) throw new InvalidOperationException($"The app ended while waiting for {what}.", run.Exception!.GetBaseException());
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Timed out waiting for {what}.");
            Thread.Sleep(5);
        }
    }

    [Fact]
    public async Task The_first_frame_paints_the_component_and_exit_ends_run()
    {
        var (app, terminal, run) = Start();

        Assert.Contains("count 0", terminal.Output);
        Assert.Contains("second", terminal.Output);

        app.Exit(3);
        Assert.Equal(3, await run);
        Assert.False(terminal.IsStarted);
    }

    [Fact]
    public async Task A_key_reaches_the_focused_handler_and_the_rerender_reaches_the_terminal()
    {
        var (app, terminal, run) = Start();
        await app.InvokeAsync(() => app.Focus.FocusAsync(app.Renderer.Root.Descendants().OfType<HostElement>().First(e => e.Focusable)));
        var framesBefore = terminal.Writes.Count;

        terminal.Inject("+");

        WaitUntil(() => terminal.Writes.Count > framesBefore, "a frame after the key");
        Assert.Equal(1, Counter.Last!.Count);
        // The frame is a diff: only the digit that changed is repainted, at its cell.
        var frame = terminal.Writes[^1];
        Assert.Contains("\e[1;7H", frame);
        Assert.Contains("1", frame.Split("\e[1;7H")[1]);
        Assert.DoesNotContain("count", frame);
        app.Exit();
        await run;
    }

    [Fact]
    public async Task Tab_moves_focus_between_focusable_elements_and_raises_focus_and_blur()
    {
        var (app, terminal, run) = Start();
        var elements = app.Renderer.Root.Descendants().OfType<HostElement>().Where(e => e.Focusable).ToList();
        Assert.Equal(2, elements.Count);

        terminal.Inject("\t");
        WaitUntil(() => ReferenceEquals(app.Focus.Focused, elements[0]) && Counter.Last!.Focused, "focus (and its handler) after Tab");

        terminal.Inject("\t");
        WaitUntil(() => ReferenceEquals(app.Focus.Focused, elements[1]) && !Counter.Last!.Focused, "focus on the second element, blur on the first");

        terminal.Inject("\e[Z");   // Shift+Tab
        WaitUntil(() => ReferenceEquals(app.Focus.Focused, elements[0]), "focus back on the first");

        app.Exit();
        await run;
    }

    [Fact]
    public async Task Ctrl_C_exits_when_no_handler_takes_it()
    {
        var (app, terminal, run) = Start();
        terminal.Inject("\x03");
        Assert.Equal(0, await run);
        Assert.False(terminal.IsStarted);
    }

    [Fact]
    public async Task A_resize_relayouts_and_repaints_in_full()
    {
        var (app, terminal, run) = Start();
        var framesBefore = terminal.Writes.Count;

        terminal.Resize(60, 20);

        WaitUntil(() => terminal.Writes.Count > framesBefore, "a frame after the resize");
        Assert.Equal(new Size(60, 20), app.Size);
        Assert.Contains("count 0", terminal.Writes[^1]);
        app.Exit();
        await run;
    }

    [Fact]
    public void RunAsync_returns_while_waiting_for_input_and_handles_it_when_it_comes()
    {
        var terminal = new HeadlessTerminal(40, 10);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });

        var code = SingleThreadContext.Run(async () =>
        {
            var run = app.RunAsync<Counter>();
            Assert.False(run.IsCompleted);
            Assert.Contains("count 0", terminal.Output);

            await app.Focus.FocusAsync(app.Renderer.Root.Descendants().OfType<HostElement>().First(e => e.Focusable));
            terminal.Inject("+");
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Counter.Last!.Count == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(5);
            }
            Assert.Equal(1, Counter.Last.Count);
            app.Exit(4);
            return await run;
        });

        Assert.Equal(4, code);
        Assert.False(terminal.IsStarted);
    }

    [Fact]
    public void Under_RunAsync_a_render_outside_the_loop_on_its_thread_still_paints()
    {
        var terminal = new HeadlessTerminal(40, 10);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });

        var painted = SingleThreadContext.Run(async () =>
        {
            var run = app.RunAsync<Counter>();
            var framesBefore = terminal.Writes.Count;
            // What a timer's callback does in a browser: InvokeAsync runs inline, since this is the loop's thread.
            await app.InvokeAsync(Counter.Last!.Increment);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (terminal.Writes.Count == framesBefore && DateTime.UtcNow < deadline)
            {
                await Task.Delay(5);
            }
            app.Exit();
            await run;
            return terminal.Writes.Count > framesBefore;
        });

        Assert.True(painted);
    }

    /// <summary>Runs async work on one thread, as a browser does.</summary>
    private sealed class SingleThreadContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public static T Run<T>(Func<Task<T>> work)
        {
            var previous = Current;
            var context = new SingleThreadContext();
            SetSynchronizationContext(context);
            try
            {
                var task = work();
                task.ContinueWith(_ => context._queue.CompleteAdding(), TaskScheduler.Default);
                foreach (var (callback, state) in context._queue.GetConsumingEnumerable())
                {
                    callback(state);
                }
                return task.GetAwaiter().GetResult();
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }
    }

    private sealed class Thrower : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b) => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void An_exception_while_rendering_restores_the_terminal_and_surfaces_from_run()
    {
        var terminal = new HeadlessTerminal(40, 10);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });

        var ex = Assert.ThrowsAny<Exception>(() => app.Run<Thrower>());

        Assert.Contains("boom", ex.Message);
        Assert.False(terminal.IsStarted);
    }
}
