using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SlopTui.Components;
using SlopTui.Input;
using SlopTui.Layout;
using SlopTui.Rendering;

namespace SlopTui.Tests.Components;

public class TerminalRendererTests
{
    private static (TerminalRenderer Renderer, TerminalDispatcher Dispatcher) Make()
    {
        var dispatcher = new TerminalDispatcher();
        dispatcher.BindToCurrentThread();
        var services = new ServiceCollection().BuildServiceProvider();
        var renderer = new TerminalRenderer(services, NullLoggerFactory.Instance, dispatcher, ex => throw ex);
        return (renderer, dispatcher);
    }

    private static HostElement Root(TerminalRenderer renderer, Type component, Dictionary<string, object?>? parameters = null)
    {
        var task = renderer.AddRootComponentAsync(component, parameters is null ? ParameterView.Empty : ParameterView.FromDictionary(parameters));
        Assert.True(task.IsCompletedSuccessfully, task.Exception?.ToString());
        return renderer.Root;
    }

    private static IEnumerable<HostElement> Elements(HostNode node) => node.Descendants().OfType<HostElement>();

    /// <summary>A component whose render tree the test supplies.</summary>
    private sealed class Fragment : ComponentBase
    {
        [Parameter] public RenderFragment? Body { get; set; }
        protected override void BuildRenderTree(RenderTreeBuilder builder) => Body?.Invoke(builder);
    }

    private static Dictionary<string, object?> Body(RenderFragment fragment) => new() { ["Body"] = fragment };

    [Fact]
    public void An_element_with_attributes_becomes_a_styled_host_element()
    {
        var (renderer, _) = Make();
        var root = Root(renderer, typeof(Fragment), Body(b =>
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "style", "display: flex; flex-direction: column; padding: 2; background: red");
            b.OpenElement(2, "span");
            b.AddAttribute(3, "style", "font-weight: bold");
            b.AddContent(4, "hi");
            b.CloseElement();
            b.CloseElement();
        }));

        var div = Assert.Single(Elements(root), e => e.Name == "div");
        Assert.Equal(Display.Flex, div.Node.Style.Display);
        Assert.Equal(FlexDirection.Column, div.Node.Style.FlexDirection);
        Assert.Equal(new Edges(2), div.Node.Style.Padding);
        Assert.Equal(Color.Red, div.Node.Style.Background);

        var span = Assert.Single(Elements(root), e => e.Name == "span");
        Assert.Equal(TextStyle.Bold, span.Node.Style.TextStyle);
        var run = Assert.Single(span.Runs);
        Assert.Equal("hi", run.Text);
        Assert.Equal(TextStyle.Bold, run.Style);
        // The span is a run in the div's anonymous text leaf, not a box of its own.
        var leaf = Assert.IsType<AnonymousTextNode>(Assert.Single(div.LayoutChildren));
        Assert.Equal("hi", leaf.Runs[0].Text);
    }

    [Fact]
    public void Components_and_regions_are_flattened_out_of_layout()
    {
        var (renderer, _) = Make();
        var root = Root(renderer, typeof(Fragment), Body(b =>
        {
            b.OpenElement(0, "div");
            b.OpenRegion(1);
            b.OpenElement(2, "p"); b.AddContent(3, "a"); b.CloseElement();
            b.OpenComponent<Fragment>(4);
            b.AddComponentParameter(5, "Body", (RenderFragment)(inner =>
            {
                inner.OpenElement(0, "p"); inner.AddContent(1, "b"); inner.CloseElement();
            }));
            b.CloseComponent();
            b.CloseRegion();
            b.CloseElement();
        }));

        var div = Elements(root).First(e => e.Name == "div");
        // Two layout children in order, though logically one is inside a region and one inside a component.
        Assert.Equal(["a", "b"], div.LayoutChildren.Cast<ElementLayoutNode>().Select(n => n.Element.Runs[0].Text));
        // Logically the div has two children: the region's contents are inlined as its children (regions have no node),
        // so the paragraph and the component container are the div's logical children.
        Assert.Equal(2, div.Children.Count);
        Assert.IsType<HostContainer>(div.Children[1]);
    }

    [Fact]
    public void A_nested_span_is_a_run_with_the_outer_style_inherited_and_overridden()
    {
        var (renderer, _) = Make();
        var root = Root(renderer, typeof(Fragment), Body(b =>
        {
            b.OpenElement(0, "p");
            b.AddAttribute(1, "style", "color: green; font-weight: bold");
            b.AddContent(2, "plain ");
            b.OpenElement(3, "span");
            b.AddAttribute(4, "style", "color: red; text-decoration: underline");
            b.AddContent(5, "loud");
            b.CloseElement();
            b.CloseElement();
        }));

        var p = Elements(root).First(e => e.Name == "p");
        Assert.IsType<AnonymousTextNode>(Assert.Single(p.LayoutChildren));   // one leaf, whatever is nested
        Assert.Equal(2, p.Runs.Count);
        Assert.Equal(("plain ", Color.Green, TextStyle.Bold), (p.Runs[0].Text, p.Runs[0].Foreground, p.Runs[0].Style));
        Assert.Equal(("loud", Color.Red, TextStyle.Bold | TextStyle.Underline), (p.Runs[1].Text, p.Runs[1].Foreground, p.Runs[1].Style));
    }

    private sealed class Toggle : ComponentBase
    {
        public bool Show { get; set; } = true;
        public string Label { get; set; } = "one";

        public void Rerender() => StateHasChanged();

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "style", "display: flex; gap: " + (Show ? 1 : 3));
            builder.OpenElement(2, "p"); builder.AddContent(3, Label); builder.CloseElement();
            if (Show)
            {
                builder.OpenElement(4, "p"); builder.AddContent(5, "two"); builder.CloseElement();
            }
            builder.CloseElement();
        }
    }

    [Fact]
    public void A_rerender_applies_attribute_text_and_removal_edits()
    {
        var (renderer, _) = Make();
        // A Fragment hosts the Toggle so a reference capture hands the instance out.
        Toggle? instance = null;
        var root = Root(renderer, typeof(Fragment), Body(b =>
        {
            b.OpenComponent<Toggle>(0);
            b.AddComponentReferenceCapture(1, o => instance = (Toggle)o);
            b.CloseComponent();
        }));
        Assert.NotNull(instance);

        var div = Elements(root).First(e => e.Name == "div");
        Assert.Equal(2, div.LayoutChildren.Count);
        Assert.Equal(1, div.Node.Style.ColumnGap);

        renderer.Dirty = false;
        instance!.Show = false;
        instance.Label = "uno";
        instance.Rerender();

        Assert.True(renderer.Dirty);
        Assert.Equal(3, div.Node.Style.ColumnGap);
        var only = Assert.Single(div.LayoutChildren);
        Assert.Equal("uno", ((ElementLayoutNode)only).Element.Runs[0].Text);
        Assert.True(div.Node.LayoutDirty);
    }

    private sealed class Keyed : ComponentBase
    {
        public List<string> Items { get; } = ["a", "b", "c"];
        public void Rerender() => StateHasChanged();

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            foreach (var item in Items)
            {
                builder.OpenElement(1, "p");
                builder.SetKey(item);
                builder.AddContent(2, item);
                builder.CloseElement();
            }
            builder.CloseElement();
        }
    }

    [Fact]
    public void Keyed_children_survive_a_reorder_as_the_same_elements()
    {
        var (renderer, _) = Make();
        Keyed? instance = null;
        var root = Root(renderer, typeof(Fragment), Body(b =>
        {
            b.OpenComponent<Keyed>(0);
            b.AddComponentReferenceCapture(1, o => instance = (Keyed)o);
            b.CloseComponent();
        }));
        var div = Elements(root).First(e => e.Name == "div");
        var before = div.LayoutChildren.Cast<ElementLayoutNode>().Select(n => n.Element).ToArray();

        instance!.Items.Reverse();
        instance.Rerender();

        var after = div.LayoutChildren.Cast<ElementLayoutNode>().Select(n => n.Element).ToArray();
        Assert.Equal(["c", "b", "a"], after.Select(e => e.Runs[0].Text));
        Assert.Same(before[0], after[2]);
        Assert.Same(before[2], after[0]);
    }

    [Fact]
    public async Task Event_handlers_are_recorded_on_the_element_and_dispatch_to_the_component()
    {
        var (renderer, _) = Make();
        KeyboardEventArgs? received = null;
        var root = Root(renderer, typeof(Fragment), Body(b =>
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(new object(), e => received = e));
            b.AddAttribute(2, "tabindex", 0);
            b.CloseElement();
        }));

        var div = Elements(root).First(e => e.Name == "div");
        Assert.True(div.Focusable);
        Assert.NotNull(div.HandlerFor("onkeydown"));
        Assert.Same(div, renderer.OwnerOf(div.HandlerFor("onkeydown")!.Value));

        var args = new KeyboardEventArgs(new KeyEvent(Key.Enter, KeyModifiers.None, ""));
        Assert.True(await renderer.RaiseAsync(div, "onkeydown", args));
        Assert.Same(args, received);
        Assert.False(await renderer.RaiseAsync(div, "onclick", args));
    }

    [Fact]
    public void The_root_body_fills_the_viewport_and_lays_out_a_component_as_blocks()
    {
        var (renderer, _) = Make();
        renderer.SetViewport(new Size(40, 10));
        var root = Root(renderer, typeof(Fragment), Body(b =>
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "style", "height: 100%; padding: 1");
            b.AddContent(2, "hello");
            b.OpenElement(3, "p"); b.AddContent(4, "para"); b.CloseElement();
            b.CloseElement();
        }));

        FlexLayout.Layout(root.Node, new Size(40, 10));
        var div = Elements(root).First(e => e.Name == "div");
        var p = Elements(root).First(e => e.Name == "p");
        Assert.Equal("body", root.Name);
        Assert.Equal(new Rect(0, 0, 40, 10), root.Node.Layout);
        Assert.Equal(new Rect(0, 0, 40, 10), div.Node.Layout);
        // Block layout: the text is a full-width line box; the paragraph follows after its margin.
        Assert.Equal(new Rect(1, 1, 38, 1), div.LayoutChildren[0].Layout);
        Assert.Equal(new Rect(1, 3, 38, 1), p.Node.Layout);

        var buffer = new CellBuffer(40, 10);
        Painter.Paint(root.Node, buffer);
        Assert.Equal(" hello", buffer.RowText(1).TrimEnd());
        Assert.Equal(" para", buffer.RowText(3).TrimEnd());
    }

    private sealed class WithRef : ComponentBase
    {
        public static WithRef? Last;
        public ElementReference Element;
        protected override void OnInitialized() => Last = this;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "id", "target");
            b.AddElementReferenceCapture(2, r => Element = r);
            b.CloseElement();
        }
    }

    [Fact]
    public void An_element_reference_resolves_to_its_host_element()
    {
        var (renderer, _) = Make();
        var root = Root(renderer, typeof(WithRef));
        var target = Elements(root).First(e => e.Id == "target");
        Assert.Same(target, renderer.Element(WithRef.Last!.Element));
        Assert.Null(renderer.Element(default));
    }
}
