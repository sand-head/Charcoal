using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using SlopTui.Components;
using SlopTui.Layout;
using SlopTui.Rendering;
using SlopTui.Terminal;

namespace SlopTui.Tests.Components;

public class CanvasTests
{
    private sealed class Host : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<Canvas>(0);
            b.AddComponentParameter(1, "style", "width: 6; height: 2; margin-left: 1");
            b.AddComponentParameter(2, nameof(Canvas.Paint), (Action<CellBuffer, Rect>)((buffer, rect) =>
                buffer.PutText(rect.X, rect.Y, "canvas", Color.Green, Color.Default, TextStyle.None)));
            b.CloseComponent();
        }
    }

    [Fact]
    public async Task The_painter_paints_into_the_elements_content_box()
    {
        var terminal = new HeadlessTerminal(20, 4);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        var run = Task.Run(() => app.Run<Host>());
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!terminal.Output.Contains("canvas") && DateTime.UtcNow < deadline) { if (run.IsFaulted) throw run.Exception!.GetBaseException(); Thread.Sleep(5); }
        var element = app.Renderer.Root.Descendants().OfType<HostElement>().First(e => e.IsCanvas);
        Assert.NotNull(element.Painter);
        Assert.Equal(new Rect(1, 0, 6, 2), element.Node.Layout);
        Assert.Contains("canvas", terminal.Output);
        app.Exit();
        await run;
    }
}
