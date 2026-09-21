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
            b.OpenElement(1, "box");
            b.AddAttribute(2, "color", "red");
            b.AddAttribute(3, "flex-direction", "column");
            b.AddContent(4, "Hello ");
            b.AddContent(5, Name);
            b.OpenElement(6, "strong"); b.AddContent(7, "!"); b.CloseElement();
            b.OpenElement(8, "box"); b.AddAttribute(9, "height", 1); b.CloseElement();
            b.AddContent(10, "\n    ");   // markup formatting between blocks
            b.OpenElement(11, "text"); b.AddContent(12, "a leaf"); b.CloseElement();
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

    [Fact]
    public void Runs_of_text_and_inline_tags_become_one_leaf_each_and_formatting_makes_none()
    {
        var (renderer, _) = Render();
        var box = renderer.Root.Descendants().OfType<HostElement>().First(e => e.Name == "box");
        var children = box.Node.Children;

        Assert.Equal(4, children.Count);
        var first = Assert.IsType<AnonymousTextNode>(children[0]);
        Assert.Equal(["Hello ", "world", "!"], first.Runs.Select(r => r.Text).ToArray());
        Assert.Equal(TextStyle.Bold, first.Runs[2].Style);
        Assert.IsType<ElementLayoutNode>(children[1], exactMatch: true);   // the empty box
        Assert.IsType<TextLayoutNode>(children[2]);                        // <text> stays its own leaf
        var div = Assert.IsType<ElementLayoutNode>(children[3], exactMatch: true);
        Assert.Equal(FlexDirection.Column, div.Style.FlexDirection);
        Assert.Equal("in a div", string.Concat(((AnonymousTextNode)div.Children[0]).Runs.Select(r => r.Text)));

        // The look is what a text child would inherit: the box's colour, through the div too.
        Assert.Equal(Color.Red, first.Style.Color);
        Assert.Equal(Color.Red, div.Children[0].Style.Color);
        Assert.Equal(new Size(12, 1), FlexLayout.Measure(first, 40, null));
    }

    [Fact]
    public void A_changed_text_part_updates_the_leaf_without_rebuilding_the_children()
    {
        var (renderer, host) = Render();
        var box = renderer.Root.Descendants().OfType<HostElement>().First(e => e.Name == "box");
        var before = box.Node.Children;
        var leaf = (AnonymousTextNode)before[0];
        _ = leaf.Runs;
        FlexLayout.Measure(leaf, 40, null);

        host.Name = "there, streaming";
        host.Refresh();

        Assert.Same(before, box.Node.Children);
        Assert.Equal("Hello there, streaming!", string.Concat(leaf.Runs.Select(r => r.Text)));
        Assert.True(leaf.LayoutDirty);
    }

    [Fact]
    public void A_restyled_ancestor_reaches_the_leaf()
    {
        var (renderer, _) = Render();
        var box = renderer.Root.Descendants().OfType<HostElement>().First(e => e.Name == "box");
        var leaf = (AnonymousTextNode)box.Node.Children[0];
        FlexLayout.Measure(leaf, 40, null);

        box.SetAttribute("color", "blue", 0);
        Assert.Equal(Color.Blue, leaf.Style.Color);
        Assert.True(leaf.LayoutDirty);
    }

    [Fact]
    public void Static_markup_with_line_breaks_reads_as_one_line_and_headings_are_bold_columns()
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
        Assert.Equal(FlexDirection.Column, div.Node.Style.FlexDirection);
        var children = div.Node.Children;
        Assert.Equal(3, children.Count);
        var h1 = (ElementLayoutNode)children[0];
        Assert.Equal(TextStyle.Bold, h1.Style.TextStyle);
        Assert.Equal(FlexDirection.Column, h1.Style.FlexDirection);
        Assert.Equal(TextStyle.Bold, ((AnonymousTextNode)h1.Children[0]).Style.TextStyle);
        var text = (AnonymousTextNode)children[1];
        Assert.Equal("Hello world, emphasised", string.Concat(text.Runs.Select(r => r.Text)));
        Assert.Equal(1, TextLayout.Measure(text.Runs, null, TextWrap.Wrap).Height);
        Assert.Equal("p", ((ElementLayoutNode)children[2]).Element.Name);
    }
}
