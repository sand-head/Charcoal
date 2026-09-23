using Charcoal.Layout;

namespace Charcoal.Tests.Layout;

public class FlexWrapTests
{
    private static readonly Style RowWrap = new() { Display = Display.Flex, FlexDirection = FlexDirection.Row, FlexWrap = FlexWrap.Wrap, ColumnGap = 1 };

    private static TextLeafNode Leaf(int width, int height, Style? style = null) => new(width, height, style);

    private static BoxNode ThreeInTen(Style style) =>
        new(style, Leaf(4, 1), Leaf(4, 1), Leaf(4, 1));

    [Fact]
    public void Items_that_do_not_fit_start_a_new_line_and_stretch_shares_the_height()
    {
        // 4 + 1 + 4 fits ten cells; a third four does not. Two lines of height
        // one share the ten rows: align-content stretch gives each five.
        var root = ThreeInTen(RowWrap);
        FlexLayout.Layout(root, new Size(10, 10));

        Assert.Equal(new Rect(0, 0, 4, 5), root.Children[0].Layout);
        Assert.Equal(new Rect(5, 0, 4, 5), root.Children[1].Layout);
        Assert.Equal(new Rect(0, 5, 4, 5), root.Children[2].Layout);
    }

    [Theory]
    [InlineData(AlignContent.FlexStart, 0, 1)]
    [InlineData(AlignContent.Center, 4, 5)]
    [InlineData(AlignContent.FlexEnd, 8, 9)]
    [InlineData(AlignContent.SpaceBetween, 0, 9)]
    [InlineData(AlignContent.SpaceAround, 2, 7)]
    public void Align_content_places_the_lines(AlignContent align, int firstLineY, int secondLineY)
    {
        var root = ThreeInTen(RowWrap with { AlignContent = align });
        FlexLayout.Layout(root, new Size(10, 10));

        Assert.Equal(firstLineY, root.Children[0].Layout.Y);
        Assert.Equal(1, root.Children[0].Layout.Height);
        Assert.Equal(secondLineY, root.Children[2].Layout.Y);
    }

    [Fact]
    public void Wrap_reverse_puts_the_first_line_last()
    {
        var root = ThreeInTen(RowWrap with { FlexWrap = FlexWrap.WrapReverse, AlignContent = AlignContent.FlexStart });
        FlexLayout.Layout(root, new Size(10, 10));

        Assert.Equal(0, root.Children[2].Layout.Y);
        Assert.Equal(1, root.Children[0].Layout.Y);
    }

    [Fact]
    public void Row_gap_separates_the_lines_of_a_row()
    {
        var root = ThreeInTen(RowWrap with { RowGap = 2, AlignContent = AlignContent.FlexStart });
        FlexLayout.Layout(root, new Size(10, 10));

        Assert.Equal(3, root.Children[2].Layout.Y);
    }

    [Fact]
    public void An_item_wider_than_the_line_takes_a_line_of_its_own_and_shrinks_to_it()
    {
        var root = new BoxNode(RowWrap with { AlignContent = AlignContent.FlexStart }, Leaf(12, 1), Leaf(3, 1));
        FlexLayout.Layout(root, new Size(10, 10));

        Assert.Equal(new Rect(0, 0, 10, 1), root.Children[0].Layout);
        Assert.Equal(new Rect(0, 1, 3, 1), root.Children[1].Layout);
    }

    [Fact]
    public void A_wrapping_column_breaks_into_columns()
    {
        var column = new Style { Display = Display.Flex, FlexDirection = FlexDirection.Column, FlexWrap = FlexWrap.Wrap };
        var root = new BoxNode(column, Leaf(2, 2), Leaf(2, 2));
        FlexLayout.Layout(root, new Size(10, 3));

        // Two rows of two do not fit three rows: the second item starts a
        // second column, and stretch shares the width between the two lines.
        Assert.Equal(new Rect(0, 0, 5, 2), root.Children[0].Layout);
        Assert.Equal(new Rect(5, 0, 5, 2), root.Children[1].Layout);
    }

    [Fact]
    public void Measuring_a_wrapping_box_counts_its_lines()
    {
        var box = ThreeInTen(RowWrap);
        Assert.Equal(new Size(9, 2), FlexLayout.Measure(box, 10, null));
        // Unbounded, one line.
        Assert.Equal(new Size(14, 1), FlexLayout.Measure(box, null, null));
    }

    [Fact]
    public void A_wrapping_box_in_a_column_is_as_tall_as_its_lines()
    {
        var inner = ThreeInTen(RowWrap with { AlignContent = AlignContent.FlexStart });
        var after = Leaf(3, 1);
        var root = new BoxNode(new Style { Display = Display.Flex, FlexDirection = FlexDirection.Column }, inner, after);
        FlexLayout.Layout(root, new Size(10, 10));

        Assert.Equal(2, inner.Layout.Height);
        Assert.Equal(2, after.Layout.Y);
    }

    [Fact]
    public void Align_items_applies_within_each_line()
    {
        var root = new BoxNode(RowWrap with { AlignItems = AlignItems.FlexEnd, AlignContent = AlignContent.FlexStart },
            Leaf(4, 3), Leaf(4, 1), Leaf(4, 1));
        FlexLayout.Layout(root, new Size(10, 10));

        // The first line is three tall; its second item sits at its bottom.
        Assert.Equal(new Rect(5, 2, 4, 1), root.Children[1].Layout);
        Assert.Equal(3, root.Children[2].Layout.Y);
    }

    [Fact]
    public void Content_size_covers_every_line()
    {
        var root = ThreeInTen(RowWrap with { AlignContent = AlignContent.FlexStart });
        FlexLayout.Layout(root, new Size(10, 10));
        Assert.Equal(new Size(9, 2), root.ContentSize);
    }
}
