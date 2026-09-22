using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SlopTui.Components;
using SlopTui.Rendering;
using SlopTui.Styling;
using SlopTui.Tests.Components.Fixtures;

namespace SlopTui.Tests.Components;

public class HtmlTagsTests
{
    private static TerminalRenderer Render(StyleContext? styles = null)
    {
        var dispatcher = new TerminalDispatcher();
        dispatcher.BindToCurrentThread();
        var renderer = new TerminalRenderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance, dispatcher, ex => throw ex, styles: styles);
        var task = renderer.AddRootComponentAsync(typeof(HtmlTagsFixture), ParameterView.Empty);
        Assert.True(task.IsCompletedSuccessfully, task.Exception?.ToString());
        return renderer;
    }

    private static IReadOnlyList<TextRun> ParagraphRuns(TerminalRenderer renderer)
    {
        var p = renderer.Root.Descendants().OfType<HostElement>().Single(e => e.Name == "p");
        return ((AnonymousTextNode)Assert.Single(p.Node.Children)).Runs;
    }

    [Fact]
    public void Inline_tags_are_runs_with_the_matching_flag_and_br_is_a_break()
    {
        var runs = ParagraphRuns(Render());

        Assert.Equal("plain bold it under gone hi\nnext red plain again", string.Concat(runs.Select(r => r.Text)));
        TextRun Run(string text) => runs.Single(r => r.Text == text);
        Assert.Equal(TextStyle.Bold, Run("bold").Style);
        Assert.Equal(TextStyle.Italic, Run("it").Style);
        Assert.Equal(TextStyle.Underline, Run("under").Style);
        Assert.Equal(TextStyle.Strikethrough, Run("gone").Style);
        Assert.Equal((Color.Yellow, Color.Black), (Run("hi").Background, Run("hi").Foreground));   // mark, as the page has it
        Assert.Equal(TextStyle.None, Run("plain ").Style);
        // A span takes a style like any element; font-weight: normal undoes the bold strong inherits.
        Assert.Equal(Color.Red, Run("red").Foreground);
        Assert.Equal(TextStyle.None, Run("plain again").Style);
    }

    [Fact]
    public void A_tag_on_its_own_under_a_div_is_an_anonymous_text_leaf()
    {
        var renderer = Render();
        var div = renderer.Root.Descendants().OfType<HostElement>().Single(e => e.Name == "div");
        var leaf = Assert.IsType<AnonymousTextNode>(Assert.Single(div.Node.Children));
        Assert.Equal("heading", string.Concat(leaf.Runs.Select(r => r.Text)));
        Assert.Equal(TextStyle.Bold, leaf.Runs[0].Style);
    }

    [Fact]
    public void A_sheet_styles_the_tag_by_its_name_over_the_user_agent_sheet()
    {
        var styles = new StyleContext();
        styles.Sheets.Add(Stylesheet.Parse("strong { color: yellow } em { font-style: normal; text-decoration: underline }"));
        var runs = ParagraphRuns(Render(styles));
        var bold = runs.Single(r => r.Text == "bold");
        Assert.Equal(Color.Yellow, bold.Foreground);
        Assert.Equal(TextStyle.Bold, bold.Style);        // the user-agent bold survives a sheet that only adds colour
        Assert.Equal(TextStyle.Underline, runs.Single(r => r.Text == "it").Style);   // and a sheet can override it
        Assert.Equal(TextStyle.None, runs.Single(r => r.Text == "plain ").Style);   // "strong" does not match the enclosing paragraph
    }
}
