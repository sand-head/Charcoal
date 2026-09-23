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
        public static Host? Last;
        public ElementReference Canvas;

        protected override void OnInitialized() => Last = this;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "canvas");
            b.AddAttribute(1, "style", "width: 6; height: 2; margin-left: 1");
            b.AddElementReferenceCapture(2, r => Canvas = r);
            b.CloseElement();
        }
    }

    [Fact]
    public async Task The_painter_paints_into_the_elements_content_box()
    {
        var terminal = new HeadlessTerminal(20, 4);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        var run = Task.Run(() => app.Run<Host>());
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Host.Last is null && DateTime.UtcNow < deadline) { if (run.IsFaulted) throw run.Exception!.GetBaseException(); Thread.Sleep(5); }

        // Set the painter on the element, the way an app does from OnAfterRender.
        await app.InvokeAsync(() =>
        {
            app.Renderer.Element(Host.Last!.Canvas)!.Painter = (buffer, rect) =>
                buffer.PutText(rect.X, rect.Y, "canvas", Color.Green, Color.Default, TextStyle.None);
            app.Invalidate();
        });
        while (!terminal.Output.Contains("canvas") && DateTime.UtcNow < deadline) { if (run.IsFaulted) throw run.Exception!.GetBaseException(); Thread.Sleep(5); }
        var element = app.Renderer.Root.Descendants().OfType<HostElement>().First(e => e.IsCanvas);
        Assert.NotNull(element.Painter);
        Assert.Equal(new Rect(1, 0, 6, 2), element.Node.Layout);
        Assert.Contains("canvas", terminal.Output);
        app.Exit();
        await run;
    }
}
