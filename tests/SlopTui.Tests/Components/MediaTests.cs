using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using SlopTui.Components;
using SlopTui.Layout;
using SlopTui.Rendering;
using SlopTui.Styling;
using SlopTui.Terminal;

namespace SlopTui.Tests.Components;

public class MediaTests
{
    private sealed class Host : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "id", "box");
            b.AddContent(2, "hello");
            b.CloseElement();
        }
    }

    private const string Css = """
        #box { padding: 0 2 }
        @media (max-width: 40) { #box { padding: 0 } }
        @media (prefers-color-scheme: light) { #box { color: black } }
        @media (prefers-reduced-motion: reduce) { #box { text-decoration: underline } }
        """;

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
    public async Task A_resize_and_the_background_answer_restyle_and_repaint()
    {
        var terminal = new HeadlessTerminal(80, 10);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        app.AddStylesheet(Css);
        var run = Task.Run(() => app.Run<Host>());
        WaitUntil(() => terminal.Writes.Count > 0, "the first frame", run);

        HostElement Box() => app.Renderer.Root.Descendants().OfType<HostElement>().First(e => e.Id == "box");
        Assert.Equal(new Edges(0, 2), Box().Node.Style.Padding);
        Assert.Equal((80, 10, ColorScheme.Dark), (app.Media.Width, app.Media.Height, app.Media.ColorScheme));
        Assert.Contains("  hello", terminal.Writes[0]);   // padded two cells in

        var frames = terminal.Writes.Count;
        terminal.Resize(30, 10);
        WaitUntil(() => Box().Node.Style.Padding == Edges.Zero && terminal.Writes.Count > frames, "the narrow rule after the resize", run);
        Assert.Equal(30, app.Media.Width);
        Assert.Contains("hello", terminal.Writes[^1]);
        Assert.DoesNotContain("  hello", terminal.Writes[^1]);   // at the edge now

        // The terminal says its background is white: light scheme.
        terminal.Inject("\e]11;rgb:ffff/ffff/ffff\e\\");
        WaitUntil(() => Box().Node.Style.Color == Color.Black, "the light scheme after the background answer", run);
        Assert.Equal(ColorScheme.Light, app.Media.ColorScheme);

        // An author-set preference restyles too, from any thread.
        await app.InvokeAsync(() => app.Media = app.Media with { ReducedMotion = true });
        WaitUntil(() => Box().Node.Style.TextStyle == TextStyle.Underline, "the reduced-motion rule", run);

        app.Exit();
        await run;
    }

    [Theory]
    [InlineData("rgb:0000/0000/0000\e\\", 0.0, 0.0, 0.0)]
    [InlineData("rgb:ffff/ffff/ffff", 1.0, 1.0, 1.0)]
    [InlineData("rgb:1c/1c/1c\a", 28 / 255.0, 28 / 255.0, 28 / 255.0)]
    [InlineData("#ff8000", 1.0, 128 / 255.0, 0.0)]
    public void The_background_answer_parses_in_its_spellings(string text, double r, double g, double b)
    {
        Assert.True(TuiApp.TryParseOscColor(text, out var color));
        Assert.Equal(r, color.R, 3);
        Assert.Equal(g, color.G, 3);
        Assert.Equal(b, color.B, 3);
        Assert.False(TuiApp.TryParseOscColor("rgb:zz/00/00", out _));
    }
}
