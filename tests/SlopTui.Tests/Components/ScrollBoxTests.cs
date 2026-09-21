using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using SlopTui.Components;
using SlopTui.Terminal;

namespace SlopTui.Tests.Components;

public class ScrollBoxTests
{
    /// <summary>Forty one-line texts inside a ScrollBox, in a 10-row terminal.</summary>
    private sealed class Host : ComponentBase
    {
        public static Host? Last;
        public ScrollBox? Box;
        public int Lines = 40;

        protected override void OnInitialized() => Last = this;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<ScrollBox>(0);
            b.AddComponentParameter(1, nameof(ScrollBox.StickToBottom), true);
            b.AddComponentParameter(2, nameof(ScrollBox.ChildContent), (RenderFragment)(inner =>
            {
                for (var i = 1; i <= Lines; i++)
                {
                    inner.OpenElement(0, "text");
                    inner.SetKey(i);
                    inner.AddContent(1, $"line {i}");
                    inner.CloseElement();
                }
            }));
            b.AddComponentReferenceCapture(3, o => Box = (ScrollBox)o);
            b.CloseComponent();
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
    public async Task The_box_learns_its_content_and_viewport_from_the_layout_and_sticks_to_the_bottom()
    {
        var terminal = new HeadlessTerminal(40, 10);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        var run = Task.Run(() => app.Run<Host>());
        WaitUntil(() => terminal.Writes.Count > 0 && Host.Last?.Box is not null, "the first frame", run);

        var box = Host.Last!.Box!;
        WaitUntil(() => box.MaxScroll == 30, "the box to learn its sizes (content 40, viewport 10)", run);
        // Sticky: it followed to the end, so the last line is on screen.
        WaitUntil(() => box.Position == 30, "the sticky scroll to land", run);
        var el = app.Renderer.Root.Descendants().OfType<HostElement>().First(e => e.Id is { } id && id.StartsWith("scrollbox-"));
        WaitUntil(() => el.Node.Style.ScrollY == 30, "the scroll-y attribute to reach the element", run);
        Thread.Sleep(100);
        var last = el.Node.Children[^1];
        Assert.Equal(new global::SlopTui.Layout.Size(40, 40), el.Node.ContentSize);
        Assert.Equal(9, last.Layout.Y);
        // The frame is a diff: "line 10" on the last row became "line 40", so the
        // write is the one cell that changed — the 4 at column 6 of row 10.
        Assert.Contains("\e[10;6H4", terminal.Writes[^1]);
        Assert.True(box.AtEnd);

        // Keys move it and unstick it.
        var element = app.Renderer.Root.Descendants().OfType<HostElement>().First(e => e.Id is { } id && id.StartsWith("scrollbox-"));
        await app.InvokeAsync(() => app.Focus.FocusAsync(element));
        var before = terminal.Writes.Count;
        terminal.Inject("\e[5~");   // PageUp
        WaitUntil(() => terminal.Writes.Count > before && box.Position == 21, "a page up", run);
        Assert.False(box.AtEnd);

        app.Exit();
        await run;
    }

    /// <summary>The gallery's shape: a row holding a bordered, padded, growing ScrollBox beside a panel.</summary>
    private sealed class RowHost : ComponentBase
    {
        public static RowHost? Last;
        public ScrollBox? Box;

        protected override void OnInitialized() => Last = this;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "box");
            b.AddAttribute(1, "gap", 1);
            b.AddAttribute(2, "flex-grow", 1);
            b.OpenComponent<ScrollBox>(3);
            b.AddComponentParameter(4, nameof(ScrollBox.FlexGrow), 1.0);
            b.AddComponentParameter(5, nameof(ScrollBox.Border), global::SlopTui.Layout.BorderStyle.Single);
            b.AddComponentParameter(6, nameof(ScrollBox.Padding), new global::SlopTui.Layout.Edges(0, 1));
            b.AddComponentParameter(7, nameof(ScrollBox.ChildContent), (RenderFragment)(inner =>
            {
                for (var i = 1; i <= 40; i++)
                {
                    inner.OpenElement(0, "text");
                    inner.SetKey(i);
                    inner.AddContent(1, $"line {i}");
                    inner.CloseElement();
                }
            }));
            b.AddComponentReferenceCapture(8, o => Box = (ScrollBox)o);
            b.CloseComponent();
            b.OpenElement(9, "box");
            b.AddAttribute(10, "width", 20);
            b.OpenElement(11, "text"); b.AddContent(12, "panel"); b.CloseElement();
            b.CloseElement();
            b.CloseElement();
        }
    }

    [Fact]
    public async Task A_bordered_growing_box_in_a_row_learns_its_sizes_too()
    {
        var terminal = new HeadlessTerminal(80, 12);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        var run = Task.Run(() => app.Run<RowHost>());
        WaitUntil(() => terminal.Writes.Count > 0 && RowHost.Last?.Box is not null, "the first frame", run);
        Thread.Sleep(100);
        var el = app.Renderer.Root.Descendants().OfType<HostElement>().First(e => e.Id is { } id && id.StartsWith("scrollbox-"));
        var box = RowHost.Last!.Box!;
        Assert.True(box.MaxScroll == 30, $"MaxScroll {box.MaxScroll}: box rect {el.Node.Layout}, content {el.Node.ContentSize}, style overflow {el.Node.Style.Overflow}, direction {el.Node.Style.FlexDirection}, children {el.Node.Children.Count}");
        app.Exit();
        await run;
    }
}
