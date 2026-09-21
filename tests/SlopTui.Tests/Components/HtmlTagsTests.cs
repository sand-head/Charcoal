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

    [Fact]
    public void Inline_tags_are_runs_with_the_matching_flag_and_br_is_a_break()
    {
        var renderer = Render();
        var texts = renderer.Root.Descendants().OfType<HostElement>().Where(e => e.IsText && !e.IsInlineText).ToList();
        var runs = texts[0].Runs;

        Assert.Equal("plain bold it under gone hi\nnext red plain again", string.Concat(runs.Select(r => r.Text)));
        TextRun Run(string text) => runs.Single(r => r.Text == text);
        Assert.Equal(TextStyle.Bold, Run("bold").Style);
        Assert.Equal(TextStyle.Italic, Run("it").Style);
        Assert.Equal(TextStyle.Underline, Run("under").Style);
        Assert.Equal(TextStyle.Strikethrough, Run("gone").Style);
        Assert.Equal(TextStyle.Inverse, Run("hi").Style);
        Assert.Equal(TextStyle.None, Run("plain ").Style);
        // <span color="red"> takes attributes like any text; <strong bold="false"> undoes the preset.
        Assert.Equal(Color.Red, Run("red").Foreground);
        Assert.Equal(TextStyle.None, Run("plain again").Style);
    }

    [Fact]
    public void A_tag_on_its_own_under_a_box_is_an_anonymous_text_leaf()
    {
        var renderer = Render();
        var box = renderer.Root.Descendants().OfType<HostElement>().Single(e => e.Name == "box");
        var leaf = Assert.IsType<AnonymousTextNode>(Assert.Single(box.Node.Children));
        Assert.Equal("heading", string.Concat(leaf.Runs.Select(r => r.Text)));
        Assert.Equal(TextStyle.Bold, leaf.Runs[0].Style);
    }

    [Fact]
    public void A_sheet_styles_the_tag_by_its_written_name_over_the_preset()
    {
        var styles = new StyleContext();
        styles.Sheets.Add(Stylesheet.Parse("strong { color: yellow } em { italic: false; underline: true }"));
        var renderer = Render(styles);
        var texts = renderer.Root.Descendants().OfType<HostElement>().Where(e => e.IsText && !e.IsInlineText).ToList();
        var runs = texts[0].Runs;
        var bold = runs.Single(r => r.Text == "bold");
        Assert.Equal(Color.Yellow, bold.Foreground);
        Assert.Equal(TextStyle.Bold, bold.Style);        // the preset survives a sheet that only adds colour
        Assert.Equal(TextStyle.Underline, runs.Single(r => r.Text == "it").Style);   // and a sheet can override it
        Assert.Equal(TextStyle.None, runs.Single(r => r.Text == "plain ").Style);   // "strong" does not match the enclosing text
    }
}
