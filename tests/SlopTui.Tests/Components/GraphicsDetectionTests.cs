using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using SlopTui.Components;
using SlopTui.Input;
using SlopTui.Layout;
using SlopTui.Rendering;
using SlopTui.Terminal;

namespace SlopTui.Tests.Components;

public class GraphicsDetectionTests
{
    private const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAYAAACp8Z5+AAAAIElEQVR4nGP8z8DwnwEKGBkYGJlgHBhgYUQoAKvBUAEAo5UDC06c1PoAAAAASUVORK5CYII=";

    private sealed class Host : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "img");
            b.AddAttribute(1, "src", Png);
            b.AddAttribute(2, "width", 4);
            b.AddAttribute(3, "height", 2);
            b.CloseElement();
        }
    }

    private static void WaitUntil(Func<bool> condition, string what, Task run)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (run.IsFaulted) throw new InvalidOperationException($"The app ended while waiting for {what}.", run.Exception!.GetBaseException());
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Timed out waiting for {what}.");
            Thread.Sleep(5);
        }
    }

    [Fact]
    public async Task The_query_goes_out_first_and_the_answers_switch_the_picture_to_placeholders()
    {
        var terminal = new HeadlessTerminal(20, 5);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        var run = Task.Run(() => app.Run<Host>());
        WaitUntil(() => terminal.Writes.Count >= 1, "the first frame", run);

        // The queries lead the first frame, in the same write.
        Assert.StartsWith(KittyGraphics.Query + Ansi.QueryCellPixels + Ansi.QueryDeviceAttributes, terminal.Writes[0]);
        Assert.False(app.Graphics.Detected);
        // Before any answer: half blocks.
        Assert.Contains("▀", terminal.Writes[0]);
        Assert.DoesNotContain(KittyGraphics.Placeholder, terminal.Writes[0]);

        var before = terminal.Writes.Count;
        terminal.Inject("\e_Gi=31;OK\e\\" + "\e[6;20;10t" + "\e[?62;4;22c");
        WaitUntil(() => terminal.Writes.Count > before, "the repaint after the answers", run);
        Thread.Sleep(50);
        Assert.True(app.Graphics.Kitty);
        Assert.True(app.Graphics.Detected);
        Assert.Equal(new Size(10, 20), app.Graphics.CellPixels);

        var frame = string.Concat(terminal.Writes.Skip(before));
        // The transmission precedes the cells that point at it, in one write.
        var transmit = frame.IndexOf("\e_Ga=T,U=1,q=2,f=32,o=z,i=", StringComparison.Ordinal);
        var placeholder = frame.IndexOf(KittyGraphics.Placeholder, StringComparison.Ordinal);
        Assert.True(transmit >= 0, frame);
        Assert.True(placeholder > transmit, frame);
        Assert.Contains(",s=4,v=4,c=4,r=2,", frame);
        Assert.Contains(KittyGraphics.Placeholder + "̅̅", frame);          // row 0 col 0
        Assert.Contains(KittyGraphics.Placeholder + "̍" + "̐", frame);    // row 1 col 3
        Assert.DoesNotContain("▀", frame);

        // A second frame does not send the image again.
        var again = terminal.Writes.Count;
        app.Invalidate();
        Thread.Sleep(80);
        Assert.DoesNotContain("a=T,", string.Concat(terminal.Writes.Skip(again)));

        app.Exit();
        await run;
        Assert.Contains("\e_Ga=d,d=I,i=", terminal.Output);
        // Nothing the terminal said became a keystroke.
        Assert.DoesNotContain("OK", terminal.Output);
    }

    [Fact]
    public async Task A_terminal_that_answers_only_da1_keeps_half_blocks()
    {
        var terminal = new HeadlessTerminal(20, 5);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        var run = Task.Run(() => app.Run<Host>());
        WaitUntil(() => terminal.Writes.Count >= 1, "the first frame", run);
        terminal.Inject("\e[?1;2c");
        WaitUntil(() => app.Graphics.Detected, "detection to close", run);
        Assert.False(app.Graphics.Kitty);
        app.Invalidate();
        Thread.Sleep(80);
        Assert.Contains("▀", terminal.Output);
        Assert.DoesNotContain(KittyGraphics.Placeholder, terminal.Output);
        app.Exit();
        await run;
        Assert.DoesNotContain("a=d,", terminal.Output);
    }

    [Fact]
    public void An_apc_reply_is_consumed_and_never_typed()
    {
        var parser = new AnsiKeyParser();
        var events = parser.Feed("\e_Gi=31;OK\e\\x", 0).Events;
        Assert.Equal(2, events.Count);
        Assert.IsType<ReplyEvent>(events[0]);
        Assert.Equal("x", Assert.IsType<KeyEvent>(events[1]).Text);

        // Split across reads: held, then completed.
        var held = parser.Feed("\e_Gi=31;O", 0);
        Assert.Empty(held.Events);
        var done = parser.Feed("K\e\\", 0);
        Assert.Equal("\e_Gi=31;OK\e\\", Assert.IsType<ReplyEvent>(Assert.Single(done.Events)).Sequence);
    }
}
