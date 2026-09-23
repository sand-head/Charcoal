using Charcoal.Layout;

namespace Charcoal.Tests.Layout;

public class ContainmentTests
{
    [Fact]
    public void An_inline_size_container_in_a_row_takes_its_width_from_flex_not_from_content()
    {
        var container = new BoxNode(new Style { Display = Display.Block, ContainerType = ContainerType.InlineSize, FlexGrow = 1 }, new TextLeafNode(30, 2));
        var sibling = new TextLeafNode(10, 1, new Style { FlexGrow = 1 });
        var row = new BoxNode(new Style { Display = Display.Flex }, container, sibling);
        FlexLayout.Layout(row, new Size(40, 5));
        // Hypothetical width 0 (contained), grown to half the row; its height is still its content's.
        Assert.Equal(new Rect(0, 0, 15, 5), container.Layout);
        Assert.Equal(2, FlexLayout.Measure(container, 15, null).Height);
        Assert.Equal(0, FlexLayout.AutomaticMinimum(container, true, 40, 5));
        Assert.Equal(2, FlexLayout.AutomaticMinimum(container, false, 40, 5));
    }

    [Fact]
    public void A_size_container_is_only_as_big_as_its_inset_and_explicit_size()
    {
        var container = new BoxNode(new Style { Display = Display.Block, ContainerType = ContainerType.Size, Padding = new Edges(1) }, new TextLeafNode(30, 2));
        var root = new BoxNode(new Style { Display = Display.Flex, FlexDirection = FlexDirection.Column, AlignItems = AlignItems.FlexStart }, container);
        FlexLayout.Layout(root, new Size(40, 10));
        Assert.Equal(new Size(2, 2), container.Layout.Size);
        Assert.Equal(new Size(10, 8), FlexLayout.Measure(new BoxNode(new Style { Display = Display.Block, ContainerType = ContainerType.Size, Width = 10, Height = 8 }, new TextLeafNode(30, 2)), 40, 10));
    }
}
