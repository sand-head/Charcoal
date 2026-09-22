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
        var nodes = Parse("""a &amp; b <div style="width: 10; padding: 1" class='card' tabindex=0 hidden><span style="color: red">hi &lt;there&gt;</span></div> tail""");

        Assert.Equal(3, nodes.Count);
        Assert.Equal("a & b ", Assert.IsType<HostTextNode>(nodes[0]).Text);
        var div = Assert.IsType<HostElement>(nodes[1]);
        Assert.Equal("div", div.Name);
        Assert.Equal(Length.Cells(10), div.Node.Style.Width);
        Assert.Equal(new Edges(1), div.Node.Style.Padding);
        Assert.Contains("card", div.Classes);
        Assert.True(div.Focusable);
        Assert.Equal(Display.None, div.Node.Style.Display);   // [hidden] in the user-agent sheet
        var span = Assert.IsType<HostElement>(Assert.Single(div.Children));
        Assert.True(span.IsInline);
        Assert.Equal(Color.Red, span.Node.Style.Color);
        Assert.Equal("hi <there>", Assert.IsType<HostTextNode>(Assert.Single(span.Children)).Text);
        Assert.Equal(" tail", Assert.IsType<HostTextNode>(nodes[2]).Text);
    }

    [Fact]
    public void Void_and_self_closing_tags_do_not_swallow_what_follows_and_comments_vanish()
    {
        var nodes = Parse("<p>one<br>two<br/>three<!-- not shown --><span/>four</p>");
        var p = Assert.IsType<HostElement>(Assert.Single(nodes));
        Assert.Equal(["one", "\n", "two", "\n", "three", "four"], p.Runs.Select(r => r.Text).ToArray());
    }

    [Fact]
    public void A_stray_less_than_and_an_unmatched_close_are_harmless()
    {
        var nodes = Parse("1 < 2 </nothing> still text");
        Assert.Equal("1 < 2  still text", string.Concat(nodes.OfType<HostTextNode>().Select(t => t.Text)));
    }

    [Fact]
    public void Text_is_kept_as_written_and_white_space_decides_at_layout()
    {
        var nodes = Parse("<div>\n    Hello\n    <strong>x</strong>\n</div>");
        var div = Assert.IsType<HostElement>(Assert.Single(nodes));
        Assert.Equal("\n    Hello\n    ", Assert.IsType<HostTextNode>(div.Children[0]).Text);
        Assert.Equal("\n", Assert.IsType<HostTextNode>(div.Children[2]).Text);
        Assert.Equal(["Hello ", "x"], div.Runs.Select(r => r.Text).ToArray());
        div.SetAttribute("style", "white-space: pre", 0);
        Assert.Equal("\n    Hello\n    x\n", string.Concat(div.Runs.Select(r => r.Text)));
    }

    /// <summary>A component whose whole tree is static markup: Razor emits one markup frame for it.</summary>
    private sealed class StaticTree : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b) =>
            b.AddMarkupContent(0, """<div style="border: solid"><p style="font-weight: bold">Title</p><p>body</p></div>""");
    }

    [Fact]
    public void A_fully_static_component_renders_as_elements_not_as_its_source()
    {
        var dispatcher = new TerminalDispatcher();
        dispatcher.BindToCurrentThread();
        var renderer = new TerminalRenderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance, dispatcher, ex => throw ex);
        var task = renderer.AddRootComponentAsync(typeof(StaticTree), ParameterView.Empty);
        Assert.True(task.IsCompletedSuccessfully, task.Exception?.ToString());

        var div = renderer.Root.Descendants().OfType<HostElement>().Single(e => e.Name == "div");
        Assert.Equal(BorderStyle.Solid, div.Node.Style.BorderStyle);
        var paragraphs = div.Node.Children;
        Assert.Equal(2, paragraphs.Count);
        Assert.Equal(TextStyle.Bold, ((AnonymousTextNode)paragraphs[0].Children[0]).Runs[0].Style);
        Assert.DoesNotContain(renderer.Root.Descendants().OfType<HostTextNode>(), t => t.Text.Contains('<'));
    }
}
