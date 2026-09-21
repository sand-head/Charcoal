using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SlopTui.Components;
using SlopTui.Rendering;
using SlopTui.Tests.Components.Fixtures;

namespace SlopTui.Tests.Components;

public class RunsTests
{
    [Fact]
    public void Runs_on_separate_markup_lines_are_one_line_of_text()
    {
        var dispatcher = new TerminalDispatcher();
        dispatcher.BindToCurrentThread();
        var renderer = new TerminalRenderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance, dispatcher, ex => throw ex);
        var task = renderer.AddRootComponentAsync(typeof(RunsFixture), ParameterView.Empty);
        Assert.True(task.IsCompletedSuccessfully, task.Exception?.ToString());

        var texts = renderer.Root.Descendants().OfType<HostElement>().Where(e => e.IsText && !e.IsInlineText).ToList();
        var runs = texts[0].Runs;
        var joined = string.Concat(runs.Select(r => r.Text));

        Assert.DoesNotContain("\n", joined);
        Assert.Equal(Color.Red, runs[0].Foreground);
        Assert.Equal("one", runs[0].Text);
        // Razor trims the leading spaces of "  two"; an explicit @("  three") keeps them.
        Assert.Contains("  three", joined);
        Assert.Equal(1, TextLayout.Measure(runs, null, global::SlopTui.Layout.TextWrap.Wrap).Height);

        // A deliberate break survives: <Newline /> is an element, not markup whitespace.
        var second = string.Concat(texts[1].Runs.Select(r => r.Text));
        Assert.Equal("a\nb", second);
    }
}
