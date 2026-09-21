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
            b.OpenElement(0, "box");
            b.AddAttribute(1, "flex-direction", "column");
            b.AddAttribute(2, "padding", 2);
            b.AddAttribute(3, "background", "red");
            b.OpenElement(4, "text");
            b.AddAttribute(5, "bold", true);
            b.AddContent(6, "hi");
            b.CloseElement();
            b.CloseElement();
        }));

        var box = Assert.Single(Elements(root), e => e.Name == "box" && !ReferenceEquals(e, root));
        Assert.Equal(FlexDirection.Column, box.Node.Style.FlexDirection);
        Assert.Equal(new Edges(2), box.Node.Style.Padding);
        Assert.Equal(Color.Red, box.Node.Style.Background);

        var text = Assert.Single(Elements(root), e => e.Name == "text");
        Assert.Equal(TextStyle.Bold, text.Node.Style.TextStyle);
        var run = Assert.Single(text.Runs);
        Assert.Equal("hi", run.Text);
        Assert.Equal(TextStyle.Bold, run.Style);
    }

    [Fact]
    public void Components_and_regions_are_flattened_out_of_layout()
    {
        var (renderer, _) = Make();
        var root = Root(renderer, typeof(Fragment), Body(b =>
        {
            b.OpenElement(0, "box");
            b.OpenRegion(1);
            b.OpenElement(2, "text"); b.AddContent(3, "a"); b.CloseElement();
            b.OpenComponent<Fragment>(4);
            b.AddComponentParameter(5, "Body", (RenderFragment)(inner =>
            {
                inner.OpenElement(0, "text"); inner.AddContent(1, "b"); inner.CloseElement();
            }));
            b.CloseComponent();
            b.CloseRegion();
            b.CloseElement();
        }));

        var box = Elements(root).First(e => e.Name == "box" && !ReferenceEquals(e, root));
        // Two layout children in order, though logically one is inside a region and one inside a component.
        Assert.Equal(["a", "b"], box.LayoutChildren.Cast<ElementLayoutNode>().Select(n => n.Element.Runs[0].Text));
        // Logically the box has one child: the region's contents are inlined as its children (regions have no node),
        // so the text and the component container are the box's logical children.
        Assert.Equal(2, box.Children.Count);
        Assert.IsType<HostContainer>(box.Children[1]);
    }

    [Fact]
    public void Nested_text_is_a_run_with_the_outer_style_inherited_and_overridden()
    {
        var (renderer, _) = Make();
        var root = Root(renderer, typeof(Fragment), Body(b =>
        {
            b.OpenElement(0, "text");
            b.AddAttribute(1, "color", "green");
            b.AddAttribute(2, "bold", true);
            b.AddContent(3, "plain ");
            b.OpenElement(4, "text");
            b.AddAttribute(5, "color", "red");
            b.AddAttribute(6, "underline", true);
            b.AddContent(7, "loud");
            b.CloseElement();
            b.CloseElement();
        }));

        var text = Elements(root).First(e => e.Name == "text");
        Assert.Empty(text.LayoutChildren);   // a text leaf has no layout children, whatever is nested
        Assert.Equal(2, text.Runs.Count);
        Assert.Equal(("plain ", Color.Green, TextStyle.Bold), (text.Runs[0].Text, text.Runs[0].Foreground, text.Runs[0].Style));
        Assert.Equal(("loud", Color.Red, TextStyle.Bold | TextStyle.Underline), (text.Runs[1].Text, text.Runs[1].Foreground, text.Runs[1].Style));
    }

    private sealed class Toggle : ComponentBase
    {
        public bool Show { get; set; } = true;
        public string Label { get; set; } = "one";

        public void Rerender() => StateHasChanged();

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "box");
            builder.AddAttribute(1, "gap", Show ? 1 : 3);
            builder.OpenElement(2, "text"); builder.AddContent(3, Label); builder.CloseElement();
            if (Show)
            {
                builder.OpenElement(4, "text"); builder.AddContent(5, "two"); builder.CloseElement();
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

        var box = Elements(root).First(e => e.Name == "box" && !ReferenceEquals(e, root));
        Assert.Equal(2, box.LayoutChildren.Count);
        Assert.Equal(1, box.Node.Style.ColumnGap);

        renderer.Dirty = false;
        instance!.Show = false;
        instance.Label = "uno";
        instance.Rerender();

        Assert.True(renderer.Dirty);
        Assert.Equal(3, box.Node.Style.ColumnGap);
        var only = Assert.Single(box.LayoutChildren);
        Assert.Equal("uno", ((ElementLayoutNode)only).Element.Runs[0].Text);
        Assert.True(box.Node.LayoutDirty);
    }

    private sealed class Keyed : ComponentBase
    {
        public List<string> Items { get; } = ["a", "b", "c"];
        public void Rerender() => StateHasChanged();

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "box");
            foreach (var item in Items)
            {
                builder.OpenElement(1, "text");
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
        var box = Elements(root).First(e => e.Name == "box" && !ReferenceEquals(e, root));
        var before = box.LayoutChildren.Cast<ElementLayoutNode>().Select(n => n.Element).ToArray();

        instance!.Items.Reverse();
        instance.Rerender();

        var after = box.LayoutChildren.Cast<ElementLayoutNode>().Select(n => n.Element).ToArray();
        Assert.Equal(["c", "b", "a"], after.Select(e => e.Runs[0].Text));
        Assert.Same(before[0], after[2]);
        Assert.Same(before[2], after[0]);
    }

    [Fact]
    public async Task Event_handlers_are_recorded_on_the_element_and_dispatch_to_the_component()
    {
        var (renderer, _) = Make();
        KeyPressEventArgs? received = null;
        var root = Root(renderer, typeof(Fragment), Body(b =>
        {
            b.OpenElement(0, "box");
            b.AddAttribute(1, "onkeypress", EventCallback.Factory.Create<KeyPressEventArgs>(new object(), e => received = e));
            b.AddAttribute(2, "focusable", true);
            b.CloseElement();
        }));

        var box = Elements(root).First(e => e.Name == "box" && !ReferenceEquals(e, root));
        Assert.True(box.Focusable);
        Assert.NotNull(box.HandlerFor("onkeypress"));
        Assert.Same(box, renderer.OwnerOf(box.HandlerFor("onkeypress")!.Value));

        var args = new KeyPressEventArgs(new KeyEvent(Key.Enter, KeyModifiers.None, ""));
        Assert.True(await renderer.RaiseAsync(box, "onkeypress", args));
        Assert.Same(args, received);
        Assert.False(await renderer.RaiseAsync(box, "onclick", args));
    }

    [Fact]
    public void The_root_box_fills_the_viewport_and_lays_out_a_component()
    {
        var (renderer, _) = Make();
        renderer.SetViewport(new Size(40, 10));
        var root = Root(renderer, typeof(Fragment), Body(b =>
        {
            b.OpenElement(0, "box");
            b.AddAttribute(1, "flex-grow", 1);
            b.AddAttribute(2, "padding", 1);
            b.OpenElement(3, "text"); b.AddContent(4, "hello"); b.CloseElement();
            b.CloseElement();
        }));

        FlexLayout.Layout(root.Node, new Size(40, 10));
        var box = Elements(root).First(e => e.Name == "box" && !ReferenceEquals(e, root));
        var text = Elements(root).First(e => e.Name == "text");
        Assert.Equal(new Rect(0, 0, 40, 10), root.Node.Layout);
        Assert.Equal(new Rect(0, 0, 40, 10), box.Node.Layout);
        // A text in a row box is stretched on the cross axis, as any flex item is; it paints its lines at the top.
        Assert.Equal(new Rect(1, 1, 5, 8), text.Node.Layout);

        var buffer = new CellBuffer(40, 10);
        Painter.Paint(root.Node, buffer);
        Assert.Equal(" hello", buffer.RowText(1).TrimEnd());
    }
}
