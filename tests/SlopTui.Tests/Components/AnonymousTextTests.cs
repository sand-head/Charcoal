using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SlopTui.Components;
using SlopTui.Layout;
using SlopTui.Rendering;

namespace SlopTui.Tests.Components;

public class AnonymousTextTests
{
    private sealed class Host : ComponentBase
    {
        public static Host? Last;
        public string Name = "world";
        public string Markup = "";
        protected override void OnInitialized() => Last = this;
        public void Refresh() => StateHasChanged();

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            if (Markup.Length > 0) { b.AddMarkupContent(0, Markup); return; }
            b.OpenElement(1, "div");
            b.AddAttribute(2, "id", "outer");
            b.AddAttribute(3, "style", "color: red");
            b.AddContent(4, "Hello ");
            b.AddContent(5, Name);
            b.OpenElement(6, "strong"); b.AddContent(7, "!"); b.CloseElement();
            b.OpenElement(8, "div"); b.AddAttribute(9, "style", "height: 1"); b.CloseElement();
            b.AddContent(10, "\n    ");   // markup formatting between blocks
            b.OpenElement(11, "span"); b.AddContent(12, "a run"); b.CloseElement();
            b.AddContent(13, " ");        // a lone space between two boxes: formatting too
            b.OpenElement(14, "div"); b.AddContent(15, "in a div"); b.CloseElement();
            b.CloseElement();
        }
    }

    private static (TerminalRenderer Renderer, Host Host) Render(string markup = "")
    {
        var dispatcher = new TerminalDispatcher();
        dispatcher.BindToCurrentThread();
        var renderer = new TerminalRenderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance, dispatcher, ex => throw ex);
        Host.Last = null;
        var task = renderer.AddRootComponentAsync(typeof(Host), ParameterView.Empty);
        Assert.True(task.IsCompletedSuccessfully, task.Exception?.ToString());
        var host = Host.Last!;
        if (markup.Length > 0) { host.Markup = markup; host.Refresh(); }
        return (renderer, host);
    }

    private static HostElement Outer(TerminalRenderer renderer) =>
        renderer.Root.Descendants().OfType<HostElement>().First(e => e.Id == "outer");

    [Fact]
    public void Runs_of_text_and_inline_elements_become_one_leaf_each_and_formatting_makes_none()
    {
        var (renderer, _) = Render();
        var children = Outer(renderer).Node.Children;

        Assert.Equal(4, children.Count);
        var first = Assert.IsType<AnonymousTextNode>(children[0]);
        Assert.Equal(["Hello ", "world", "!"], first.Runs.Select(r => r.Text).ToArray());
        Assert.Equal(TextStyle.Bold, first.Runs[2].Style);
        Assert.IsType<ElementLayoutNode>(children[1], exactMatch: true);   // the empty div
        var run = Assert.IsType<AnonymousTextNode>(children[2]);           // the span, with the formatting around it collapsed away
        Assert.Equal("a run", string.Concat(run.Runs.Select(r => r.Text)));
        var div = Assert.IsType<ElementLayoutNode>(children[3], exactMatch: true);
        Assert.Equal(Display.Block, div.Style.Display);
        Assert.Equal("in a div", string.Concat(((AnonymousTextNode)div.Children[0]).Runs.Select(r => r.Text)));

        // The look is the block's inherited text properties: the colour, through the div too.
        Assert.Equal(Color.Red, first.Style.Color);
        Assert.Equal(Color.Red, first.Runs[0].Foreground);
        Assert.Equal(Color.Red, div.Children[0].Style.Color);
        Assert.Equal(new Size(12, 1), FlexLayout.Measure(first, 40, null));
    }

    [Fact]
    public void A_changed_text_part_updates_the_leaf_without_rebuilding_the_children()
    {
        var (renderer, host) = Render();
        var outer = Outer(renderer);
        var before = outer.Node.Children;
        var leaf = (AnonymousTextNode)before[0];
        _ = leaf.Runs;
        FlexLayout.Measure(leaf, 40, null);

        host.Name = "there, streaming";
        host.Refresh();

        Assert.Same(before, outer.Node.Children);
        Assert.Equal("Hello there, streaming!", string.Concat(leaf.Runs.Select(r => r.Text)));
        Assert.True(leaf.LayoutDirty);
    }

    private sealed class Typing : ComponentBase
    {
        public static Typing? Last;
        public string Text = "";
        protected override void OnInitialized() => Last = this;
        public void Refresh() => StateHasChanged();

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddMarkupContent(1, "<span>❯ </span>");
            b.AddContent(2, Text);
            b.CloseElement();
        }
    }

    [Fact]
    public void Text_that_was_empty_joins_the_leaf_when_it_gets_content()
    {
        var dispatcher = new TerminalDispatcher();
        dispatcher.BindToCurrentThread();
        var renderer = new TerminalRenderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance, dispatcher, ex => throw ex);
        var task = renderer.AddRootComponentAsync(typeof(Typing), ParameterView.Empty);
        Assert.True(task.IsCompletedSuccessfully, task.Exception?.ToString());
        var div = renderer.Root.Descendants().OfType<HostElement>().First(e => e.Name == "div");
        var leaf = (AnonymousTextNode)Assert.Single(div.Node.Children);
        Assert.Equal("❯", string.Concat(leaf.Runs.Select(r => r.Text)));   // normal: the trailing space goes

        Typing.Last!.Text = "hi";
        Typing.Last.Refresh();
        leaf = (AnonymousTextNode)Assert.Single(div.Node.Children);
        Assert.Equal("❯ hi", string.Concat(leaf.Runs.Select(r => r.Text)));

        Typing.Last.Text = "";
        Typing.Last.Refresh();
        Assert.Equal("❯", string.Concat(((AnonymousTextNode)Assert.Single(div.Node.Children)).Runs.Select(r => r.Text)));
    }

    [Fact]
    public void A_restyled_ancestor_reaches_the_leaf_and_its_runs()
    {
        var (renderer, _) = Render();
        var outer = Outer(renderer);
        var leaf = (AnonymousTextNode)outer.Node.Children[0];
        FlexLayout.Measure(leaf, 40, null);

        outer.SetAttribute("style", "color: blue", 0);
        Assert.Equal(Color.Blue, leaf.Style.Color);
        Assert.Equal(Color.Blue, leaf.Runs[0].Foreground);
        Assert.True(leaf.LayoutDirty);
    }

    [Fact]
    public void Static_markup_reads_as_a_page_would_blocks_stacked_and_inline_text_collapsed()
    {
        var (renderer, _) = Render("""
            <div>
                <h1>Title</h1>
                Hello
                world, <em>emphasised</em>
                <p>a paragraph</p>
            </div>
            """);
        var div = renderer.Root.Descendants().OfType<HostElement>().Single(e => e.Name == "div");
        Assert.Equal(Display.Block, div.Node.Style.Display);
        var children = div.Node.Children;
        Assert.Equal(3, children.Count);
        var h1 = (ElementLayoutNode)children[0];
        Assert.Equal(TextStyle.Bold, h1.Style.TextStyle);
        Assert.Equal(Display.Block, h1.Style.Display);
        Assert.Equal(TextStyle.Bold, ((AnonymousTextNode)h1.Children[0]).Style.TextStyle);
        var text = (AnonymousTextNode)children[1];
        Assert.Equal("Hello world, emphasised", string.Concat(text.Runs.Select(r => r.Text)));
        Assert.Equal(TextStyle.Italic, text.Runs[^1].Style);
        Assert.Equal(1, TextLayout.Measure(text.Runs, null, TextWrap.Wrap).Height);
        Assert.Equal("p", ((ElementLayoutNode)children[2]).Element.Name);

        // Laid out as blocks: the heading's margin, the text, the paragraph's margin, collapsed between neighbours.
        FlexLayout.Layout(renderer.Root.Node, new Size(40, 12));
        Assert.Equal(new Rect(0, 1, 40, 1), h1.Layout);       // margin-block 1 above (nothing to collapse with at the root's edge? the body has no inset, so it escapes: see below)
        Assert.Equal(new Rect(0, 3, 40, 1), text.Layout);     // 1 below the heading
        Assert.Equal(new Rect(0, 5, 40, 1), children[2].Layout);
    }

    [Fact]
    public void A_pre_keeps_its_whitespace_as_a_leaf()
    {
        var (renderer, _) = Render("<pre>  two\n   three</pre>");
        var pre = renderer.Root.Descendants().OfType<HostElement>().Single(e => e.Name == "pre");
        var leaf = (AnonymousTextNode)Assert.Single(pre.Node.Children);
        Assert.Equal("  two\n   three", string.Concat(leaf.Runs.Select(r => r.Text)));
        Assert.Equal(TextWrap.Clip, leaf.Style.Wrap);
        Assert.Equal(new Size(8, 2), FlexLayout.Measure(leaf, 40, null));
    }
}
