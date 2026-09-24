using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Charcoal.Components;
using Charcoal.Layout;
using Charcoal.Rendering;
using Charcoal.Terminal;

namespace Charcoal.Tests.Components;

public class ContainerQueryTests
{
    private sealed class Host : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div"); b.AddAttribute(1, "class", "row");
            b.OpenElement(2, "div"); b.AddAttribute(3, "class", "panel"); b.AddAttribute(4, "id", "panel");
            b.OpenElement(5, "div"); b.AddAttribute(6, "class", "label"); b.AddAttribute(7, "id", "label"); b.AddContent(8, "label"); b.CloseElement();
            b.OpenElement(9, "div"); b.AddAttribute(10, "class", "inner"); b.AddAttribute(11, "id", "inner");
            b.OpenElement(12, "div"); b.AddAttribute(13, "id", "deep"); b.AddContent(14, "deep"); b.CloseElement();
            b.CloseElement();
            b.CloseElement();
            b.OpenElement(15, "div"); b.AddAttribute(16, "class", "side"); b.AddContent(17, "side"); b.CloseElement();
            b.CloseElement();
        }
    }

    private const string Css = """
        .row { display: flex; height: 100% }
        .panel { container: panel / inline-size; flex: 1 }
        .side { width: 20 }
        .inner { container-type: inline-size; padding: 0 5 }
        @container panel (max-width: 30) { .label { color: red } }
        @container (min-width: 40) { #deep { color: green } }
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
    public async Task Rules_follow_the_containers_box_across_a_resize_and_name_picks_the_container()
    {
        var terminal = new HeadlessTerminal(80, 10);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        app.AddStylesheet(Css);
        var run = AppThread.Start<Host>(app);
        WaitUntil(() => terminal.Writes.Count > 0, "the first frame", run);
        HostElement ById(string id) => app.Renderer.Root.Descendants().OfType<HostElement>().First(e => e.Id == id);

        // The panel is 60 wide (80 minus the 20-wide side): not narrow, and
        // #deep's nearest container is .inner at 50 (60 minus padding), so green.
        Assert.Equal(60, ById("panel").ContainerEnvironment!.Width);
        Assert.Equal(50, ById("inner").ContainerEnvironment!.Width);
        Assert.Equal(Color.Default, ById("label").Node.Style.Color);
        Assert.Equal(Color.Green, ById("deep").Node.Style.Color);

        // Narrower: the panel is 30 wide, the label turns red; .inner is 20, #deep loses its green.
        terminal.Resize(50, 10);
        WaitUntil(() => ById("label").Node.Style.Color == Color.Red, "the narrow panel rule", run);
        Assert.Equal(Color.Default, ById("deep").Node.Style.Color);
        Assert.Equal(30, ById("panel").ContainerEnvironment!.Width);
        // The painted frame shows the rule took effect.
        Assert.Contains(Color.Red.ToSgr(true), string.Concat(terminal.Writes));

        app.Exit();
        await run;
    }
}
