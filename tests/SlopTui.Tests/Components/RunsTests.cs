using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SlopTui.Components;
using SlopTui.Layout;
using SlopTui.Rendering;
using SlopTui.Tests.Components.Fixtures;

namespace SlopTui.Tests.Components;

public class RunsTests
{
    private static TerminalRenderer Render()
    {
        var dispatcher = new TerminalDispatcher();
        dispatcher.BindToCurrentThread();
        var renderer = new TerminalRenderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance, dispatcher, ex => throw ex);
        var task = renderer.AddRootComponentAsync(typeof(RunsFixture), ParameterView.Empty);
        Assert.True(task.IsCompletedSuccessfully, task.Exception?.ToString());
        return renderer;
    }

    private static AnonymousTextNode Leaf(TerminalRenderer renderer, string id) =>
        (AnonymousTextNode)Assert.Single(renderer.Root.Descendants().OfType<HostElement>().Single(e => e.Id == id).Node.Children);

    [Fact]
    public void Under_normal_white_space_the_line_breaks_between_runs_are_single_spaces()
    {
        var leaf = Leaf(Render(), "normal");
        var runs = leaf.Runs;
        var joined = string.Concat(runs.Select(r => r.Text));

        // Razor trims the leading spaces of "  two"; the explicit @("  three") keeps them, and normal collapses them.
        Assert.Equal("one two three", joined);
        Assert.Equal(Color.Red, runs[0].Foreground);
        Assert.Equal("one", runs[0].Text);
        Assert.Equal(1, TextLayout.Measure(runs, null, TextWrap.Wrap).Height);
    }

    [Fact]
    public void Under_pre_wrap_every_space_and_newline_is_kept()
    {
        var leaf = Leaf(Render(), "pre");
        var joined = string.Concat(leaf.Runs.Select(r => r.Text));

        // Razor drops the whitespace at the start and end of the div's content and around the code block; the rest is kept.
        Assert.Equal("one\n      two\n      three four", joined);
        Assert.Equal(WhiteSpace.PreWrap, leaf.Style.WhiteSpace);
    }

    [Fact]
    public void A_br_is_a_line_break_that_collapsing_keeps()
    {
        var leaf = Leaf(Render(), "break");
        Assert.Equal("a\nb", string.Concat(leaf.Runs.Select(r => r.Text)));
        Assert.Equal(2, TextLayout.Measure(leaf.Runs, null, TextWrap.Wrap).Height);
    }
}
