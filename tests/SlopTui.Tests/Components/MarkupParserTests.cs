using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SlopTui.Components;
using SlopTui.Layout;
using SlopTui.Rendering;

namespace SlopTui.Tests.Components;

public class MarkupParserTests
{
    private static List<HostNode> Parse(string markup) => MarkupParser.Parse(markup, name => new HostElement(name));

    [Fact]
    public void Elements_attributes_text_and_entities_come_back_as_nodes()
    {
        var nodes = Parse("""a &amp; b <box width="10" padding='1' flex-grow=1 focusable><text color="red">hi &lt;there&gt;</text></box> tail""");

        Assert.Equal(3, nodes.Count);
        Assert.Equal("a & b ", Assert.IsType<HostTextNode>(nodes[0]).Text);
        var box = Assert.IsType<HostElement>(nodes[1]);
        Assert.Equal("box", box.Name);
        Assert.Equal(Length.Cells(10), box.Node.Style.Width);
        Assert.Equal(new Edges(1), box.Node.Style.Padding);
        Assert.Equal(1, box.Node.Style.FlexGrow);
        Assert.True(box.Focusable);
        var text = Assert.IsType<HostElement>(Assert.Single(box.Children));
        Assert.True(text.IsText);
        Assert.Equal(Color.Red, text.Node.Style.Color);
        Assert.Equal("hi <there>", Assert.IsType<HostTextNode>(Assert.Single(text.Children)).Text);
        Assert.Equal(" tail", Assert.IsType<HostTextNode>(nodes[2]).Text);
    }

    [Fact]
    public void Void_and_self_closing_tags_do_not_swallow_what_follows_and_comments_vanish()
    {
        var nodes = Parse("<text>one<br>two<br/>three<!-- not shown --><span/>four</text>");
        var text = Assert.IsType<HostElement>(Assert.Single(nodes));
        Assert.Equal("one\ntwo\nthree four".Replace(" ", ""), string.Concat(text.Runs.Select(r => r.Text)).Replace(" ", ""));
        Assert.Equal(["one", "\n", "two", "\n", "three", "four"], text.Runs.Select(r => r.Text).ToArray());
    }

    [Fact]
    public void A_stray_less_than_and_an_unmatched_close_are_harmless()
    {
        var nodes = Parse("1 < 2 </nothing> still text");
        Assert.Equal("1 < 2  still text", string.Concat(nodes.OfType<HostTextNode>().Select(t => t.Text)));
    }

    [Fact]
    public void Newlines_in_markup_are_formatting()
    {
        Assert.Equal("Hello world", MarkupParser.CollapseNewlines("\n    Hello\n    world\n"));
        Assert.Equal(" Hello world ", MarkupParser.CollapseNewlines("\n    Hello\n    world\n", first: false, last: false));
        Assert.Equal("a b", MarkupParser.CollapseNewlines("a\r\n  b"));
        Assert.Equal("a  b", MarkupParser.CollapseNewlines("a  b"));          // spaces without a newline stay
        Assert.Equal("\n    ", MarkupParser.CollapseNewlines("\n    "));    // whitespace-only stays for the run collector to drop
        var nodes = Parse("<div>\n    Hello\n    <strong>x</strong>\n</div>");
        var div = Assert.IsType<HostElement>(Assert.Single(nodes));
        Assert.Equal("Hello ", Assert.IsType<HostTextNode>(div.Children[0]).Text);
        Assert.Equal("\n", Assert.IsType<HostTextNode>(div.Children[2]).Text);
    }

    /// <summary>A component whose whole tree is static markup: Razor emits one markup frame for it.</summary>
    private sealed class StaticTree : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b) =>
            b.AddMarkupContent(0, """<box flex-direction="column" border="single"><text bold="true">Title</text><text>body</text></box>""");
    }

    [Fact]
    public void A_fully_static_component_renders_as_elements_not_as_its_source()
    {
        var dispatcher = new TerminalDispatcher();
        dispatcher.BindToCurrentThread();
        var renderer = new TerminalRenderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance, dispatcher, ex => throw ex);
        var task = renderer.AddRootComponentAsync(typeof(StaticTree), ParameterView.Empty);
        Assert.True(task.IsCompletedSuccessfully, task.Exception?.ToString());

        var box = renderer.Root.Descendants().OfType<HostElement>().Single(e => e.Name == "box" && e != renderer.Root);
        Assert.Equal(BorderStyle.Single, box.Node.Style.Border);
        Assert.Equal(FlexDirection.Column, box.Node.Style.FlexDirection);
        var texts = box.Node.Children;
        Assert.Equal(2, texts.Count);
        Assert.Equal(TextStyle.Bold, ((TextLayoutNode)texts[0]).Element.Runs[0].Style);
        Assert.DoesNotContain(renderer.Root.Descendants().OfType<HostTextNode>(), t => t.Text.Contains('<'));
    }
}
