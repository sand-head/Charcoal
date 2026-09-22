using SlopTui.Components;
using SlopTui.Layout;
using SlopTui.Rendering;
using SlopTui.Terminal;
using SlopTui.Tests.Components.Fixtures;

namespace SlopTui.Tests.Components;

public class ScopedCssTests
{
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
    public void The_build_embeds_the_bundle_and_the_app_loads_it()
    {
        var app = new TuiApp(new HeadlessTerminal(40, 10), new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        var sheet = app.AddScopedStylesheets(typeof(ScopedFixture).Assembly);
        Assert.NotNull(sheet);
        Assert.Contains(sheet.Rules, r => r.Selectors.Any(s => s.Text.StartsWith(".scoped[b-")));
    }

    [Fact]
    public async Task A_scoped_rule_styles_the_components_own_elements_and_deep_reaches_a_childs()
    {
        var terminal = new HeadlessTerminal(40, 10);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        var run = Task.Run(() => app.Run<ScopedFixture>());
        WaitUntil(() => terminal.Writes.Count > 0, "the first frame", run);

        HostElement ById(string id) => app.Renderer.Root.Descendants().OfType<HostElement>().First(e => e.Id == id);
        var own = ById("own");
        var child = ById("child");
        Assert.Contains(own.Attributes.Keys, k => k.StartsWith("b-"));   // the compiler stamped the scope
        Assert.Equal(Color.Cyan, own.Node.Style.Color);
        Assert.Equal(new Edges(1), own.Node.Style.Padding);
        Assert.Equal(Color.Cyan, ById("own-text").Node.Style.Color);   // inherited
        Assert.Equal(Edges.Zero, child.Node.Style.Padding);            // same class, other component: not matched
        Assert.Equal(TextStyle.Underline, ById("child-text").Node.Style.TextStyle);   // ::deep

        app.Exit();
        await run;
    }
}
