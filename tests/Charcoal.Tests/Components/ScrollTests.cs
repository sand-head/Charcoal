using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Charcoal.Components;
using Charcoal.Terminal;

namespace Charcoal.Tests.Components;

public class ScrollTests
{
    /// <summary>
    /// Lines in a scrolling div, in a short terminal. <see cref="Pinned"/>
    /// adds the web's pin-to-bottom stylesheet: every line ineligible, one
    /// sentinel of non-zero height at the end eligible.
    /// </summary>
    private sealed class Host : ComponentBase
    {
        public static Host? Last;
        public ElementReference Box;
        public int Lines = 40;
        public int Leading;                 // rows of content above the lines
        public bool Pinned;
        public string ContainerAnchor = "auto";
        public int Scrolls;

        protected override void OnInitialized() => Last = this;

        public void Refresh() => StateHasChanged();

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "style", $"overflow: auto; height: 100%; overflow-anchor: {ContainerAnchor}");
            b.AddAttribute(2, "tabindex", 0);
            b.AddAttribute(3, "onscroll", EventCallback.Factory.Create<ScrollEventArgs>(this, _ => Scrolls++));
            b.AddElementReferenceCapture(4, r => Box = r);

            if (Leading > 0)
            {
                b.OpenElement(5, "div");
                b.SetKey("leading");
                b.AddAttribute(6, "style", $"height: {Leading}" + (Pinned ? "; overflow-anchor: none" : ""));
                b.AddContent(7, "leading");
                b.CloseElement();
            }
            for (var i = 1; i <= Lines; i++)
            {
                b.OpenElement(8, "div");
                b.SetKey(i);
                if (Pinned) b.AddAttribute(9, "style", "overflow-anchor: none");
                b.AddContent(10, $"line {i}");
                b.CloseElement();
            }
            if (Pinned)
            {
                // The sentinel, the only element left eligible to anchor.
                b.OpenElement(11, "div");
                b.SetKey("sentinel");
                b.AddAttribute(12, "style", "height: 0");
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

    /// <summary>A running app over <see cref="Host"/>, with the box to hand.</summary>
    private sealed class Running : IAsyncDisposable
    {
        public readonly HeadlessTerminal Terminal;
        public readonly TuiApp App;
        private readonly Task _run;

        public Running(int width = 40, int height = 10, Action<Host>? setup = null, int wheelRows = 3)
        {
            Host.Last = null;
            Terminal = new HeadlessTerminal(width, height);
            App = new TuiApp(Terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero, WheelRows = wheelRows });
            _run = Task.Run(() => App.Run<Host>());
            WaitUntil(() => Terminal.Writes.Count > 0 && Host.Last is not null, "the first frame", _run);
            if (setup is not null) App.InvokeAsync(() => { setup(Host.Last!); Host.Last!.Refresh(); }).GetAwaiter().GetResult();
        }

        public Host Component => Host.Last!;
        public HostElement Box => App.Renderer.Element(Host.Last!.Box)!;
        public void Until(Func<bool> condition, string what) => WaitUntil(condition, what, _run);

        public async ValueTask DisposeAsync()
        {
            App.Exit();
            await _run;
        }
    }

    [Fact]
    public async Task A_scroll_container_clamps_its_offset_to_the_content()
    {
        await using var running = new Running();
        running.Until(() => running.Box.ScrollTopMax == 30, "the box to learn its sizes (content 40, viewport 10)");

        // Nothing is eligible to anchor below the fold, so it starts at the
        // top, as a page does.
        Assert.Equal(0, running.Box.ScrollTop);

        await running.App.InvokeAsync(() => running.Box.ScrollTop = 999);
        running.Until(() => running.Box.ScrollTop == 30, "the offset to clamp to the end");

        // Content shrinks under the offset: the clamp pulls it back into range.
        await running.App.InvokeAsync(() => { running.Component.Lines = 12; running.Component.Refresh(); });
        running.Until(() => running.Box.ScrollTopMax == 2 && running.Box.ScrollTop == 2, "the offset to follow shrinking content");
        Assert.True(running.Component.Scrolls > 0, "the scroll event never fired");
    }

    [Fact]
    public async Task A_sentinel_at_the_end_pins_the_box_to_the_bottom()
    {
        // The web's technique, unchanged: every line overflow-anchor: none,
        // one sentinel of non-zero height left eligible at the end.
        await using var running = new Running(setup: h => { h.Pinned = true; h.Lines = 12; });
        running.Until(() => running.Box.ScrollTopMax > 0, "content taller than the box");

        // Scroll to the end once, as a page must be scrolled once before
        // anchoring has anything to hold on to.
        await running.App.InvokeAsync(() => running.Box.ScrollTop = running.Box.ScrollTopMax);
        running.Until(() => running.Box.ScrollTop == running.Box.ScrollTopMax, "the end in view");

        for (var i = 0; i < 5; i++)
        {
            var lines = running.Component.Lines + 1;
            await running.App.InvokeAsync(() => { running.Component.Lines = lines; running.Component.Refresh(); });
            running.Until(() => running.Box.ScrollHeight == lines, $"the layout to see {lines} lines");
            running.Until(() => running.Box.ScrollTop == running.Box.ScrollTopMax, $"the pin to hold at {lines} lines");
        }

        Assert.Equal(17, running.Component.Lines);
        // The sentinel holds the line just past the last row, and costs none.
        var port = running.Box.Scrollport;
        var sentinel = running.Box.ChildElements().Last();
        Assert.Equal(0, sentinel.Node.Layout.Height);
        Assert.Equal(port.Bottom, sentinel.Node.Layout.Y);
    }

    [Fact]
    public async Task A_scroll_to_the_end_and_a_new_line_in_the_same_frame_still_pin()
    {
        await using var running = new Running(setup: h => { h.Pinned = true; h.Lines = 12; });
        running.Until(() => running.Box.ScrollTopMax == 2, "content taller than the box");

        // Both land before the next layout, so the anchor must be chosen at
        // the new offset from the old layout, as a browser does.
        await running.App.InvokeAsync(() =>
        {
            running.Box.ScrollTop = running.Box.ScrollTopMax;
            running.Component.Lines = 13;
            running.Component.Refresh();
        });

        running.Until(() => running.Box.ScrollHeight == 13, "the layout to see 13 lines");
        running.Until(() => running.Box.ScrollTop == 3, "the pin to follow the new line");
    }

    [Fact]
    public async Task Content_growing_above_the_view_does_not_shove_it_down()
    {
        // Anchoring's everyday job, and the reason the property exists: an
        // earlier element getting taller must not move what you are reading.
        await using var running = new Running(setup: h => h.Leading = 3);
        running.Until(() => running.Box.ScrollTopMax == 33, "the box to learn its sizes");

        await running.App.InvokeAsync(() => running.Box.ScrollTop = 20);
        running.Until(() => running.Box.ScrollTop == 20, "the offset to land");
        Thread.Sleep(50);

        // Where a line in view sits on screen right now.
        static int RowOfLine(Running r, int line) =>
            r.Box.ChildElements().ElementAt(line).Node.Layout.Y;   // [0] is the leading block
        var before = RowOfLine(running, 21);
        Assert.InRange(before, running.Box.Scrollport.Y, running.Box.Scrollport.Bottom - 1);

        // The block above the viewport grows by four rows.
        await running.App.InvokeAsync(() => { running.Component.Leading = 7; running.Component.Refresh(); });
        running.Until(() => running.Box.ScrollTop == 24, "the offset to absorb the growth above");
        Thread.Sleep(50);

        Assert.Equal(before, RowOfLine(running, 21));
    }

    [Fact]
    public async Task Overflow_anchor_none_on_the_container_switches_anchoring_off()
    {
        await using var running = new Running(setup: h => { h.ContainerAnchor = "none"; h.Leading = 3; });
        running.Until(() => running.Box.ScrollTopMax == 33, "the box to learn its sizes");
        await running.App.InvokeAsync(() => running.Box.ScrollTop = 20);
        running.Until(() => running.Box.ScrollTop == 20, "the offset to land");

        await running.App.InvokeAsync(() => { running.Component.Leading = 7; running.Component.Refresh(); });
        running.Until(() => running.Box.ScrollTopMax == 37, "the grown content");
        Thread.Sleep(100);

        // No anchoring: the offset stays put and the content slides under it.
        Assert.Equal(20, running.Box.ScrollTop);
    }

    [Fact]
    public async Task The_arrow_keys_scroll_the_focused_container_with_no_handler_of_its_own()
    {
        await using var running = new Running();
        running.Until(() => running.Box.ScrollTopMax == 30, "the box to learn its sizes");
        await running.App.InvokeAsync(() => running.App.Focus.FocusAsync(running.Box));
        var scrolls = running.Component.Scrolls;

        running.Terminal.Inject("\e[F");    // End
        running.Until(() => running.Box.ScrollTop == 30, "end");
        Assert.True(running.Component.Scrolls > scrolls, "the scroll event did not fire for the key");

        running.Terminal.Inject("\e[5~");   // PageUp: a viewport less one
        running.Until(() => running.Box.ScrollTop == 21, "a page up");
        running.Terminal.Inject("\eOA");    // Up
        running.Until(() => running.Box.ScrollTop == 20, "one row up");
        running.Terminal.Inject("\e[H");    // Home
        running.Until(() => running.Box.ScrollTop == 0, "home");

        // Clamped at the top: another Up goes nowhere.
        running.Terminal.Inject("\eOA");
        Thread.Sleep(50);
        Assert.Equal(0, running.Box.ScrollTop);
    }

    [Fact]
    public async Task The_wheel_scrolls_the_container_under_the_pointer()
    {
        await using var running = new Running();
        running.Until(() => running.Box.ScrollTopMax == 30, "the box to learn its sizes");
        await running.App.InvokeAsync(() => running.Box.ScrollTop = 10);
        running.Until(() => running.Box.ScrollTop == 10, "the offset to land");

        // SGR wheel over cell (1,1), which is inside the box. Nothing is
        // focused and no handler exists: the wheel is the UA's alone.
        running.Terminal.Inject("\e[<64;1;1M");
        running.Until(() => running.Box.ScrollTop == 7, "a wheel notch of three rows up");
        running.Terminal.Inject("\e[<65;1;1M");
        running.Until(() => running.Box.ScrollTop == 10, "a notch back down");
    }

}
