using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using SlopTui.Components;
using SlopTui.Terminal;

namespace SlopTui.Tests.Components;

public class ScrollTests
{
    /// <summary>Forty one-line texts in a scrolling div, in a short terminal.</summary>
    private sealed class Host : ComponentBase
    {
        public static Host? Last;
        public ElementReference Box;
        public int Lines = 40;
        public string Anchor = "auto";
        public int Scrolls;

        protected override void OnInitialized() => Last = this;

        public void Refresh() => StateHasChanged();

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "style", $"overflow: auto; height: 100%; overflow-anchor: {Anchor}");
            b.AddAttribute(2, "tabindex", 0);
            b.AddAttribute(3, "onscroll", EventCallback.Factory.Create<ScrollEventArgs>(this, _ => Scrolls++));
            b.AddElementReferenceCapture(4, r => Box = r);
            for (var i = 1; i <= Lines; i++)
            {
                b.OpenElement(5, "div");
                b.SetKey(i);
                b.AddContent(6, $"line {i}");
                b.CloseElement();
            }
            b.CloseElement();
        }
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
    public async Task A_scroll_container_clamps_to_its_content_and_anchors_to_the_end()
    {
        var terminal = new HeadlessTerminal(40, 10);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        var run = Task.Run(() => app.Run<Host>());
        WaitUntil(() => terminal.Writes.Count > 0 && Host.Last is not null, "the first frame", run);

        HostElement Box() => app.Renderer.Element(Host.Last!.Box)!;
        WaitUntil(() => Box().ScrollTopMax == 30, "the box to learn its sizes (content 40, viewport 10)", run);
        // overflow-anchor: auto — it followed to the end, so the last line shows.
        WaitUntil(() => Box().ScrollTop == 30, "the anchored scroll to land", run);
        Thread.Sleep(100);
        Assert.Equal(new global::SlopTui.Layout.Size(40, 40), Box().Node.ContentSize);
        Assert.Equal(9, Box().Node.Children[^1].Layout.Y);

        // The anchoring happens between the layout and the paint, so the very
        // first painted frame is already at the end. The component this
        // replaced could only correct the offset after a frame had been
        // painted, which showed the top of the content for one frame first.
        var firstPainted = terminal.Writes.First(w => w.Contains("line ", StringComparison.Ordinal));
        Assert.Contains("line 40", firstPainted);
        Assert.DoesNotContain("line 1 ", firstPainted);
        Assert.True(Host.Last!.Scrolls > 0, "the scroll event never fired");

        app.Exit();
        await run;
    }

    [Fact]
    public async Task The_arrow_keys_scroll_the_focused_container_with_no_handler_of_its_own()
    {
        var terminal = new HeadlessTerminal(40, 10);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        var run = Task.Run(() => app.Run<Host>());
        WaitUntil(() => terminal.Writes.Count > 0 && Host.Last is not null, "the first frame", run);
        HostElement Box() => app.Renderer.Element(Host.Last!.Box)!;
        WaitUntil(() => Box().ScrollTop == 30, "the anchored scroll to land", run);

        await app.InvokeAsync(() => app.Focus.FocusAsync(Box()));
        var scrolls = Host.Last!.Scrolls;

        terminal.Inject("\e[5~");   // PageUp: a viewport less one
        WaitUntil(() => Box().ScrollTop == 21, "a page up", run);
        // Scrolling away from the end unanchors it: appended content no longer follows.
        Assert.True(Host.Last!.Scrolls > scrolls, "the scroll event did not fire for the key");

        terminal.Inject("\eOA");    // Up
        WaitUntil(() => Box().ScrollTop == 20, "one row up", run);
        terminal.Inject("\e[H");    // Home
        WaitUntil(() => Box().ScrollTop == 0, "home", run);
        // Clamped at the top: another Up goes nowhere.
        terminal.Inject("\eOA");
        Thread.Sleep(50);
        Assert.Equal(0, Box().ScrollTop);

        terminal.Inject("\e[F");    // End
        WaitUntil(() => Box().ScrollTop == 30, "end", run);

        app.Exit();
        await run;
    }

    [Fact]
    public async Task The_wheel_scrolls_the_container_under_the_pointer()
    {
        var terminal = new HeadlessTerminal(40, 10);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero, WheelRows = 3 });
        var run = Task.Run(() => app.Run<Host>());
        WaitUntil(() => terminal.Writes.Count > 0 && Host.Last is not null, "the first frame", run);
        HostElement Box() => app.Renderer.Element(Host.Last!.Box)!;
        WaitUntil(() => Box().ScrollTop == 30, "the anchored scroll to land", run);

        // SGR wheel up over cell (1,1), which is inside the box. Nothing is
        // focused and no handler exists: the wheel is the UA's alone.
        terminal.Inject("\e[<64;1;1M");
        WaitUntil(() => Box().ScrollTop == 27, "a wheel notch of three rows", run);
        terminal.Inject("\e[<65;1;1M");
        WaitUntil(() => Box().ScrollTop == 30, "a notch back down", run);

        app.Exit();
        await run;
    }

    [Fact]
    public async Task Overflow_anchor_none_leaves_the_offset_where_it_is()
    {
        var terminal = new HeadlessTerminal(40, 10);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        var run = Task.Run(() => app.Run<Host>());
        WaitUntil(() => terminal.Writes.Count > 0 && Host.Last is not null, "the first frame", run);
        HostElement Box() => app.Renderer.Element(Host.Last!.Box)!;
        WaitUntil(() => Box().ScrollTopMax == 30, "the box to learn its sizes", run);

        // Unanchor it, park it at the top, then grow the content: it stays put.
        await app.InvokeAsync(() =>
        {
            Host.Last!.Anchor = "none";
            Host.Last.Lines = 60;
            Host.Last.Refresh();
        });
        await app.InvokeAsync(() => Box().ScrollTop = 0);
        WaitUntil(() => Box().ScrollTopMax == 50, "the grown content", run);
        Thread.Sleep(100);
        Assert.Equal(0, Box().ScrollTop);

        app.Exit();
        await run;
    }
}
