using SlopTui.Components;
using SlopTui.Layout;
using SlopTui.Rendering;
using SlopTui.Terminal;
using SlopTui.Tests.Components.Fixtures;

namespace SlopTui.Tests.Components;

public class StyledComponentsTests
{
    private const string Css = """
        .panel { padding: 0 1; border: single }
        .panel:focus { border: double; border-color: green }
        .panel:focus-within text { color: green }
        .panel.hot text { color: red; bold: true }
        """;

    private static (TuiApp App, HeadlessTerminal Terminal, Task<int> Run) Start()
    {
        var terminal = new HeadlessTerminal(40, 12);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        app.AddStylesheet(Css);
        var run = Task.Run(() => app.Run<StyledFixture>());
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

    private static HostElement ById(TuiApp app, string id) =>
        app.Renderer.Root.Descendants().OfType<HostElement>().First(e => e.Id == id);

    [Fact]
    public async Task A_class_rule_styles_a_rendered_component()
    {
        var (app, _, run) = Start();

        var first = ById(app, "first");
        Assert.Equal(new Edges(0, 1), first.Node.Style.Padding);
        Assert.Equal(BorderStyle.Single, first.Node.Style.Border);
        Assert.Equal(Color.Default, ById(app, "first-text").Node.Style.Color);

        app.Exit();
        await run;
    }

    [Fact]
    public async Task Focus_restyles_the_element_and_its_descendants_both_ways()
    {
        var (app, terminal, run) = Start();
        var first = ById(app, "first");
        var second = ById(app, "second");

        terminal.Inject("\t");
        WaitUntil(() => ReferenceEquals(app.Focus.Focused, first) && first.Node.Style.Border == BorderStyle.Double, "the first panel focused and restyled");
        Assert.Equal(Color.Green, first.Node.Style.BorderColor);
        Assert.Equal(Color.Green, ById(app, "first-text").Node.Style.Color);
        Assert.Equal(BorderStyle.Single, second.Node.Style.Border);

        terminal.Inject("\t");
        WaitUntil(() => ReferenceEquals(app.Focus.Focused, second) && second.Node.Style.Border == BorderStyle.Double, "the second panel focused and restyled");
        Assert.Equal(BorderStyle.Single, first.Node.Style.Border);
        Assert.Equal(Color.Default, ById(app, "first-text").Node.Style.Color);
        Assert.Equal(Color.Green, ById(app, "second-text").Node.Style.Color);

        // The painted frame carries the green through the ANSI, so the change reached the terminal.
        WaitUntil(() => terminal.Writes[^1].Contains(Color.Green.ToSgr(true)), "a frame painted in green");

        app.Exit();
        await run;
    }

    [Fact]
    public async Task A_class_toggled_by_a_rerender_re_resolves_the_text_below_it()
    {
        var (app, terminal, run) = Start();
        var text = ById(app, "second-text");
        Assert.Equal(Color.Default, text.Node.Style.Color);

        terminal.Inject("\t");
        WaitUntil(() => app.Focus.Focused is not null, "focus");
        terminal.Inject("h");
        WaitUntil(() => text.Node.Style.Color == Color.Red, "the hot class to reach the text");
        Assert.Equal(TextStyle.Bold, text.Node.Style.TextStyle);
        Assert.Contains("hot", ById(app, "second").Classes);

        terminal.Inject("h");
        WaitUntil(() => text.Node.Style.Color == Color.Default, "the hot class to leave the text");

        app.Exit();
        await run;
    }

    [Fact]
    public async Task A_sheet_added_while_running_restyles_everything()
    {
        var (app, terminal, run) = Start();
        var framesBefore = terminal.Writes.Count;

        await app.InvokeAsync(() => app.AddStylesheet("#root { padding: 2 } .panel { border: round }"));

        WaitUntil(() => ById(app, "root").Node.Style.Padding == new Edges(2), "the new sheet applied");
        Assert.Equal(BorderStyle.Round, ById(app, "first").Node.Style.Border);
        WaitUntil(() => terminal.Writes.Count > framesBefore, "a repaint");

        app.Exit();
        await run;
    }
}
