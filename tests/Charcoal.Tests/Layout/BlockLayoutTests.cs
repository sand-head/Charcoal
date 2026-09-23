using Charcoal.Layout;

namespace Charcoal.Tests.Layout;

public class BlockLayoutTests
{
    private static readonly Style Block = new() { Display = Display.Block };

    private static TextLeafNode Leaf(int width, int height, Style? style = null) => new(width, height, style);

    private static void Lay(LayoutNode root, int width = 40, int height = 10) => FlexLayout.Layout(root, new Size(width, height));

    [Fact]
    public void Children_stack_top_to_bottom_at_the_full_width()
    {
        var root = new BoxNode(Block, Leaf(5, 1), Leaf(3, 2));
        Lay(root);
        Assert.Equal(new Rect(0, 0, 40, 1), root.Children[0].Layout);
        Assert.Equal(new Rect(0, 1, 40, 2), root.Children[1].Layout);
        Assert.Equal(new Size(40, 3), root.ContentSize);
    }

    [Fact]
    public void The_initial_display_lays_out_as_a_block_and_the_root_is_as_tall_as_the_viewport()
    {
        var root = new BoxNode(new Style(), Leaf(5, 1));
        Lay(root, 20, 5);
        Assert.Equal(new Rect(0, 0, 20, 5), root.Layout);
        Assert.Equal(new Rect(0, 0, 20, 1), root.Children[0].Layout);
    }

    [Fact]
    public void An_explicit_width_is_kept_and_left_aligned_and_a_percentage_is_of_the_container()
    {
        var root = new BoxNode(Block, Leaf(5, 1, new Style { Width = 10 }), Leaf(5, 1, new Style { Width = Length.Percent(50) }));
        Lay(root);
        Assert.Equal(new Rect(0, 0, 10, 1), root.Children[0].Layout);
        Assert.Equal(new Rect(0, 1, 20, 1), root.Children[1].Layout);
    }

    [Fact]
    public void Fit_content_width_hugs_the_content_where_auto_would_stretch()
    {
        var root = new BoxNode(Block, Leaf(5, 1, new Style { Width = Length.FitContent, Padding = new Edges(0, 1) }));
        Lay(root);
        Assert.Equal(new Rect(0, 0, 7, 1), root.Children[0].Layout);
        Assert.Equal(Length.FitContent, StyleParser.Apply(Style.Default, "width", "fit-content").Width);
    }

    [Fact]
    public void Flex_properties_on_block_children_do_nothing()
    {
        var root = new BoxNode(Block, Leaf(5, 1, new Style { FlexGrow = 1 }), Leaf(5, 1));
        Lay(root, 40, 10);
        Assert.Equal(new Rect(0, 0, 40, 1), root.Children[0].Layout);
        Assert.Equal(new Rect(0, 1, 40, 1), root.Children[1].Layout);
    }

    [Fact]
    public void Vertical_margins_between_neighbours_collapse_to_the_larger()
    {
        var root = new BoxNode(Block with { Padding = new Edges(1, 0) },
            Leaf(5, 1, new Style { Margin = new Edges(2, 0) }),
            Leaf(5, 1, new Style { Margin = new Edges(1, 0) }),
            Leaf(5, 1, new Style { Margin = new Edges(3, 0) }));
        Lay(root);
        // padding 1, margin 2, leaf, max(2,1)=2, leaf, max(1,3)=3, leaf
        Assert.Equal(1 + 2, root.Children[0].Layout.Y);
        Assert.Equal(1 + 2 + 1 + 2, root.Children[1].Layout.Y);
        Assert.Equal(1 + 2 + 1 + 2 + 1 + 3, root.Children[2].Layout.Y);
        Assert.Equal(new Size(40, 2 + 1 + 2 + 1 + 3 + 1 + 3), root.ContentSize);
    }

    [Fact]
    public void Horizontal_margins_do_not_collapse_and_shrink_the_stretched_width()
    {
        var root = new BoxNode(Block, Leaf(5, 1, new Style { Margin = new Edges(0, 2, 0, 3) }));
        Lay(root);
        Assert.Equal(new Rect(3, 0, 35, 1), root.Children[0].Layout);
    }

    [Fact]
    public void A_first_childs_top_margin_escapes_a_plain_container_and_collapses_outside_it()
    {
        // outer > inner (no inset) > leaf with margin-top 2: the 2 moves out of
        // the inner box, so the inner box starts at 2 and the leaf at its top.
        var inner = new BoxNode(Block, Leaf(5, 1, new Style { Margin = new Edges(2, 0, 1, 0) }));
        var after = Leaf(5, 1, new Style { Margin = new Edges(3, 0, 0, 0) });
        var outer = new BoxNode(Block, inner, after);
        Lay(outer);
        Assert.Equal(2, inner.MarginTopThrough);
        Assert.Equal(1, inner.MarginBottomThrough);
        Assert.Equal(new Rect(0, 2, 40, 1), inner.Layout);
        Assert.Equal(new Rect(0, 2, 40, 1), inner.Children[0].Layout);
        // The inner box's escaped bottom margin (1) collapses with the next sibling's top margin (3).
        Assert.Equal(new Rect(0, 6, 40, 1), after.Layout);
    }

    [Fact]
    public void A_border_or_padding_keeps_a_childs_margin_inside()
    {
        var inner = new BoxNode(Block with { Padding = new Edges(1, 0) }, Leaf(5, 1, new Style { Margin = new Edges(2, 0) }));
        var outer = new BoxNode(Block, inner);
        Lay(outer);
        Assert.Equal(0, inner.MarginTopThrough);
        Assert.Equal(new Rect(0, 0, 40, 1 + 2 + 1 + 2 + 1), inner.Layout);
        Assert.Equal(new Rect(0, 3, 40, 1), inner.Children[0].Layout);
    }

    [Fact]
    public void Nothing_escapes_a_flex_item_or_the_root()
    {
        var item = new BoxNode(Block, Leaf(5, 1, new Style { Margin = new Edges(2, 0) }));
        var row = new BoxNode(new Style { Display = Display.Flex }, item);
        Lay(row);
        Assert.Equal(0, item.MarginTopThrough);
        Assert.Equal(new Rect(0, 2, 5, 1), item.Children[0].Layout);   // the item is fit-content wide (5), stretched tall

        var root = new BoxNode(Block, Leaf(5, 1, new Style { Margin = new Edges(2, 0) }));
        Lay(root);
        Assert.Equal(new Rect(0, 2, 40, 1), root.Children[0].Layout);
    }

    [Fact]
    public void A_percentage_height_resolves_against_a_definite_container_only()
    {
        var definite = new BoxNode(Block with { Height = 8 }, Leaf(5, 1, new Style { Height = Length.Percent(50) }));
        Lay(new BoxNode(Block, definite));
        Assert.Equal(8, definite.Layout.Height);
        Assert.Equal(4, definite.Children[0].Layout.Height);

        var auto = new BoxNode(Block, Leaf(5, 1, new Style { Height = Length.Percent(50) }));
        Lay(new BoxNode(Block, auto));
        Assert.Equal(1, auto.Children[0].Layout.Height);   // auto: the content's
    }

    [Fact]
    public void A_scrolling_block_shifts_its_children_and_has_a_zero_minimum()
    {
        var box = new BoxNode(Block with { Overflow = Overflow.Scroll, Height = 3 }) { ScrollTop = 2 };
        for (var i = 0; i < 6; i++) box.Add(Leaf(4, 1));
        Lay(box);
        Assert.Equal(-2, box.Children[0].Layout.Y);
        Assert.Equal(0, box.Children[2].Layout.Y);
        Assert.Equal(6, box.ContentSize.Height);
        Assert.Equal(0, FlexLayout.AutomaticMinimum(box, false, 40, 10));
    }

    [Fact]
    public void A_block_inside_a_flex_row_is_as_wide_as_its_widest_child_and_shrinks_to_its_longest_word()
    {
        var block = new BoxNode(Block, Leaf(6, 1, new Style { MinWidth = 6 }), Leaf(4, 2));
        var row = new BoxNode(new Style { Display = Display.Flex }, block, Leaf(10, 1));
        Lay(row);
        Assert.Equal(new Rect(0, 0, 6, 10), block.Layout);
        Assert.Equal(new Rect(0, 0, 6, 1), block.Children[0].Layout);
        Assert.Equal(6, FlexLayout.MinContentWidth(block));   // the widest child's minimum
    }
}
