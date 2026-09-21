using SlopTui.Layout;

namespace SlopTui.Tests.Layout;

public class FlexLayoutTests
{
    private static readonly Style Row = new() { FlexDirection = FlexDirection.Row };
    private static readonly Style Column = new() { FlexDirection = FlexDirection.Column };

    private static TextLeafNode Leaf(int width, int height, Style? style = null) => new(width, height, style);

    private static TextLeafNode Wrapping(int columns = 40, Style? style = null) => new(
        (availW, _) =>
        {
            var width = availW is { } w ? Math.Min(w, columns) : columns;
            var rows = width <= 0 ? 0 : (columns + width - 1) / width;
            return new Size(width, rows);
        }, style);

    private static void Lay(LayoutNode root, int width = 40, int height = 10) => FlexLayout.Layout(root, new Size(width, height));

    [Fact]
    public void The_root_takes_the_viewport()
    {
        var root = new BoxNode(Row);
        Lay(root, 80, 24);
        Assert.Equal(new Rect(0, 0, 80, 24), root.Layout);
    }

    [Fact]
    public void Row_places_children_left_to_right_at_their_widths()
    {
        var root = new BoxNode(Row, Leaf(5, 1), Leaf(3, 1));
        Lay(root);
        Assert.Equal(new Rect(0, 0, 5, 10), root.Children[0].Layout);
        Assert.Equal(new Rect(5, 0, 3, 10), root.Children[1].Layout);
    }

    [Fact]
    public void Column_places_children_top_to_bottom_stretched_across()
    {
        var root = new BoxNode(Column, Leaf(5, 1), Leaf(3, 2));
        Lay(root);
        Assert.Equal(new Rect(0, 0, 40, 1), root.Children[0].Layout);
        Assert.Equal(new Rect(0, 1, 40, 2), root.Children[1].Layout);
    }

    [Fact]
    public void Row_reverse_mirrors_the_main_axis()
    {
        var root = new BoxNode(Row with { FlexDirection = FlexDirection.RowReverse }, Leaf(5, 1), Leaf(3, 1));
        Lay(root);
        Assert.Equal(new Rect(35, 0, 5, 10), root.Children[0].Layout);
        Assert.Equal(new Rect(32, 0, 3, 10), root.Children[1].Layout);
    }

    [Fact]
    public void Column_reverse_puts_the_first_child_at_the_bottom()
    {
        var root = new BoxNode(Column with { FlexDirection = FlexDirection.ColumnReverse }, Leaf(5, 1), Leaf(3, 2));
        Lay(root);
        Assert.Equal(new Rect(0, 9, 40, 1), root.Children[0].Layout);
        Assert.Equal(new Rect(0, 7, 40, 2), root.Children[1].Layout);
    }

    [Fact]
    public void Grow_shares_the_free_space_by_weight()
    {
        var root = new BoxNode(Row,
            Leaf(0, 1, new Style { FlexGrow = 1 }),
            Leaf(0, 1, new Style { FlexGrow = 3 }));
        Lay(root);
        Assert.Equal(10, root.Children[0].Layout.Width);
        Assert.Equal(30, root.Children[1].Layout.Width);
    }

    [Fact]
    public void Grow_with_content_adds_free_space_on_top_of_it()
    {
        var root = new BoxNode(Row,
            Leaf(10, 1, new Style { FlexGrow = 1 }),
            Leaf(10, 1));
        Lay(root);
        Assert.Equal(30, root.Children[0].Layout.Width);
        Assert.Equal(new Rect(30, 0, 10, 10), root.Children[1].Layout);
    }

    [Fact]
    public void Rounding_remainders_go_to_items_so_the_row_sums_exactly()
    {
        var root = new BoxNode(Row,
            Leaf(0, 1, new Style { FlexGrow = 1 }),
            Leaf(0, 1, new Style { FlexGrow = 1 }),
            Leaf(0, 1, new Style { FlexGrow = 1 }));
        Lay(root, 40);
        var widths = root.Children.Select(c => c.Layout.Width).ToArray();
        Assert.Equal(40, widths.Sum());
        Assert.All(widths, w => Assert.InRange(w, 13, 14));
        Assert.Equal(40, root.Children[2].Layout.Right);
    }

    [Fact]
    public void Shrink_takes_the_overflow_weighted_by_basis()
    {
        var root = new BoxNode(Row, Leaf(30, 1), Leaf(30, 1));
        Lay(root, 40);
        Assert.Equal(20, root.Children[0].Layout.Width);
        Assert.Equal(20, root.Children[1].Layout.Width);
        Assert.Equal(40, root.Children[1].Layout.Right);
    }

    [Fact]
    public void Shrink_zero_keeps_an_item_at_its_size_and_the_other_takes_the_loss()
    {
        var root = new BoxNode(Row,
            Leaf(30, 1, new Style { FlexShrink = 0 }),
            Leaf(30, 1));
        Lay(root, 40);
        Assert.Equal(30, root.Children[0].Layout.Width);
        Assert.Equal(10, root.Children[1].Layout.Width);
    }

    [Fact]
    public void Max_width_freezes_a_growing_item_and_the_rest_take_what_it_could_not()
    {
        var root = new BoxNode(Row,
            Leaf(0, 1, new Style { FlexGrow = 1, MaxWidth = 5 }),
            Leaf(0, 1, new Style { FlexGrow = 1 }));
        Lay(root, 40);
        Assert.Equal(5, root.Children[0].Layout.Width);
        Assert.Equal(35, root.Children[1].Layout.Width);
    }

    [Fact]
    public void Min_width_freezes_a_shrinking_item()
    {
        var root = new BoxNode(Row,
            Leaf(30, 1, new Style { MinWidth = 25 }),
            Leaf(30, 1));
        Lay(root, 40);
        Assert.Equal(25, root.Children[0].Layout.Width);
        Assert.Equal(15, root.Children[1].Layout.Width);
    }

    [Fact]
    public void Flex_basis_overrides_content_as_the_starting_size()
    {
        var root = new BoxNode(Row,
            Leaf(30, 1, new Style { FlexBasis = 0, FlexGrow = 1 }),
            Leaf(2, 1, new Style { FlexBasis = 0, FlexGrow = 1 }));
        Lay(root, 40);
        Assert.Equal(20, root.Children[0].Layout.Width);
        Assert.Equal(20, root.Children[1].Layout.Width);
    }

    [Fact]
    public void Percent_flex_basis_is_of_the_container_main_size()
    {
        var root = new BoxNode(Row, Leaf(1, 1, new Style { FlexBasis = Length.Percent(25) }));
        Lay(root, 40);
        Assert.Equal(10, root.Children[0].Layout.Width);
    }

    [Fact]
    public void Explicit_width_and_height_are_used_as_given()
    {
        var root = new BoxNode(Row, Leaf(1, 1, new Style { Width = 7, Height = 3 }));
        Lay(root);
        Assert.Equal(new Rect(0, 0, 7, 3), root.Children[0].Layout);
    }

    [Fact]
    public void Percent_sizes_are_of_the_parent_content_box()
    {
        var root = new BoxNode(Row with { Padding = new Edges(1) },
            Leaf(1, 1, new Style { Width = Length.Percent(50), Height = Length.Percent(50) }));
        Lay(root, 42, 12);
        Assert.Equal(new Rect(1, 1, 20, 5), root.Children[0].Layout);
    }

    [Fact]
    public void Percent_inside_percent_compounds()
    {
        var inner = new BoxNode(Row with { Width = Length.Percent(50) }, Leaf(1, 1, new Style { Width = Length.Percent(50) }));
        var root = new BoxNode(Row with { Width = Length.Percent(50) }, inner);
        Lay(root, 80, 4);
        // The root always takes the viewport; its percentage width is ignored at the top.
        Assert.Equal(40, inner.Layout.Width);
        Assert.Equal(20, inner.Children[0].Layout.Width);
    }

    [Fact]
    public void Padding_insets_the_children()
    {
        var root = new BoxNode(Column with { Padding = new Edges(1, 2) }, Leaf(5, 1));
        Lay(root, 40, 10);
        Assert.Equal(new Rect(2, 1, 36, 1), root.Children[0].Layout);
    }

    [Fact]
    public void A_border_insets_by_one_on_each_drawn_side()
    {
        var root = new BoxNode(Column with { Border = BorderStyle.Single, BorderLeft = false }, Leaf(5, 1));
        Lay(root, 40, 10);
        Assert.Equal(new Rect(0, 1, 39, 1), root.Children[0].Layout);
    }

    [Fact]
    public void Margin_sits_outside_the_child_rect_and_counts_toward_the_flow()
    {
        var root = new BoxNode(Row,
            Leaf(5, 1, new Style { Margin = new Edges(1, 2) }),
            Leaf(5, 1));
        Lay(root, 40, 10);
        Assert.Equal(new Rect(2, 1, 5, 8), root.Children[0].Layout);
        Assert.Equal(new Rect(9, 0, 5, 10), root.Children[1].Layout);
    }

    [Fact]
    public void Column_gap_separates_items_in_a_row()
    {
        var root = new BoxNode(Row with { ColumnGap = 3, RowGap = 9 }, Leaf(5, 1), Leaf(5, 1), Leaf(5, 1));
        Lay(root);
        Assert.Equal(0, root.Children[0].Layout.X);
        Assert.Equal(8, root.Children[1].Layout.X);
        Assert.Equal(16, root.Children[2].Layout.X);
    }

    [Fact]
    public void Row_gap_separates_items_in_a_column()
    {
        var root = new BoxNode(Column with { RowGap = 2, ColumnGap = 9 }, Leaf(5, 1), Leaf(5, 1));
        Lay(root);
        Assert.Equal(0, root.Children[0].Layout.Y);
        Assert.Equal(3, root.Children[1].Layout.Y);
    }

    [Fact]
    public void Gaps_are_taken_before_free_space_is_grown_into()
    {
        var root = new BoxNode(Row with { ColumnGap = 4 },
            Leaf(0, 1, new Style { FlexGrow = 1 }),
            Leaf(0, 1, new Style { FlexGrow = 1 }));
        Lay(root, 40);
        Assert.Equal(18, root.Children[0].Layout.Width);
        Assert.Equal(new Rect(22, 0, 18, 10), root.Children[1].Layout);
    }

    [Theory]
    [InlineData(JustifyContent.FlexStart, 0, 5)]
    [InlineData(JustifyContent.Center, 15, 20)]
    [InlineData(JustifyContent.FlexEnd, 30, 35)]
    [InlineData(JustifyContent.SpaceBetween, 0, 35)]
    [InlineData(JustifyContent.SpaceAround, 7, 27)]
    [InlineData(JustifyContent.SpaceEvenly, 10, 25)]
    public void Justify_content_places_two_five_wide_items_in_forty(JustifyContent justify, int first, int second)
    {
        var root = new BoxNode(Row with { JustifyContent = justify }, Leaf(5, 1), Leaf(5, 1));
        Lay(root, 40);
        Assert.Equal(first, root.Children[0].Layout.X);
        Assert.Equal(second, root.Children[1].Layout.X);
    }

    [Theory]
    [InlineData(AlignItems.Stretch, 0, 10)]
    [InlineData(AlignItems.FlexStart, 0, 2)]
    [InlineData(AlignItems.Center, 4, 2)]
    [InlineData(AlignItems.FlexEnd, 8, 2)]
    public void Align_items_places_a_two_high_item_in_ten_rows(AlignItems align, int y, int height)
    {
        var root = new BoxNode(Row with { AlignItems = align }, Leaf(5, 2));
        Lay(root, 40, 10);
        Assert.Equal(y, root.Children[0].Layout.Y);
        Assert.Equal(height, root.Children[0].Layout.Height);
    }

    [Fact]
    public void Align_self_overrides_align_items_for_one_item()
    {
        var root = new BoxNode(Row with { AlignItems = AlignItems.FlexStart },
            Leaf(5, 2),
            Leaf(5, 2, new Style { AlignSelf = AlignSelf.FlexEnd }));
        Lay(root, 40, 10);
        Assert.Equal(0, root.Children[0].Layout.Y);
        Assert.Equal(8, root.Children[1].Layout.Y);
    }

    [Fact]
    public void An_explicit_cross_size_is_not_stretched()
    {
        var root = new BoxNode(Row, Leaf(5, 2, new Style { Height = 3 }));
        Lay(root, 40, 10);
        Assert.Equal(3, root.Children[0].Layout.Height);
    }

    [Fact]
    public void Display_none_takes_no_space_and_gets_an_empty_rect()
    {
        var root = new BoxNode(Row,
            Leaf(5, 1, new Style { Display = Display.None }),
            Leaf(5, 1));
        Lay(root);
        Assert.Equal(Rect.Empty, root.Children[0].Layout);
        Assert.Equal(0, root.Children[1].Layout.X);
    }

    [Fact]
    public void Absolute_children_leave_the_flow_and_sit_at_their_offsets_in_the_padding_box()
    {
        var root = new BoxNode(Row with { Border = BorderStyle.Single, Padding = new Edges(1) },
            Leaf(5, 1, new Style { Position = Position.Absolute, Top = 2, Left = 3, Width = 4, Height = 2 }),
            Leaf(5, 1));
        Lay(root, 40, 10);
        // The padding box starts inside the border at (1,1); the flow child is unaffected.
        Assert.Equal(new Rect(4, 3, 4, 2), root.Children[0].Layout);
        Assert.Equal(2, root.Children[1].Layout.X);
    }

    [Fact]
    public void Absolute_right_and_bottom_offsets_anchor_to_the_far_edges()
    {
        var root = new BoxNode(Row,
            Leaf(5, 2, new Style { Position = Position.Absolute, Right = 1, Bottom = 1 }));
        Lay(root, 40, 10);
        Assert.Equal(new Rect(34, 7, 5, 2), root.Children[0].Layout);
    }

    [Fact]
    public void Absolute_left_and_right_together_set_the_width()
    {
        var root = new BoxNode(Row,
            Leaf(5, 2, new Style { Position = Position.Absolute, Left = 2, Right = 3 }));
        Lay(root, 40, 10);
        Assert.Equal(new Rect(2, 0, 35, 2), root.Children[0].Layout);
    }

    [Fact]
    public void A_text_leaf_wraps_to_the_column_width_and_grows_in_height()
    {
        var root = new BoxNode(Column, Wrapping(40));
        Lay(root, 10, 20);
        Assert.Equal(new Rect(0, 0, 10, 4), root.Children[0].Layout);
    }

    [Fact]
    public void A_text_leaf_in_a_row_takes_its_natural_width_when_it_fits()
    {
        var root = new BoxNode(Row, Wrapping(12), Leaf(3, 1));
        Lay(root, 40, 5);
        Assert.Equal(12, root.Children[0].Layout.Width);
        Assert.Equal(12, root.Children[1].Layout.X);
    }

    [Fact]
    public void A_text_leaf_in_a_row_shrinks_when_it_does_not_fit()
    {
        var root = new BoxNode(Row, Wrapping(40), Leaf(10, 1, new Style { FlexShrink = 0 }));
        Lay(root, 40, 5);
        Assert.Equal(30, root.Children[0].Layout.Width);
        Assert.Equal(30, root.Children[1].Layout.X);
    }

    [Fact]
    public void A_box_measures_its_content_from_its_children()
    {
        var inner = new BoxNode(Column with { Padding = new Edges(1) }, Leaf(6, 2), Leaf(4, 1));
        var root = new BoxNode(Row with { AlignItems = AlignItems.FlexStart }, inner);
        Lay(root, 40, 10);
        // 6 wide + 2 padding, 3 tall + 2 padding.
        Assert.Equal(new Rect(0, 0, 8, 5), inner.Layout);
        Assert.Equal(new Rect(1, 1, 6, 2), inner.Children[0].Layout);
        Assert.Equal(new Rect(1, 3, 6, 1), inner.Children[1].Layout);
    }

    [Fact]
    public void Nested_grow_distributes_at_every_level()
    {
        var left = new BoxNode(Column with { FlexGrow = 1 },
            Leaf(0, 0, new Style { FlexGrow = 1 }),
            Leaf(0, 0, new Style { FlexGrow = 1 }));
        var right = new BoxNode(Column with { FlexGrow = 1 });
        var root = new BoxNode(Row, left, right);
        Lay(root, 40, 10);
        Assert.Equal(new Rect(0, 0, 20, 10), left.Layout);
        Assert.Equal(new Rect(20, 0, 20, 10), right.Layout);
        Assert.Equal(new Rect(0, 0, 20, 5), left.Children[0].Layout);
        Assert.Equal(new Rect(0, 5, 20, 5), left.Children[1].Layout);
    }

    [Fact]
    public void A_column_with_a_growing_middle_pins_the_header_and_footer()
    {
        var root = new BoxNode(Column,
            Leaf(0, 2),
            Leaf(0, 0, new Style { FlexGrow = 1 }),
            Leaf(0, 1));
        Lay(root, 80, 24);
        Assert.Equal(new Rect(0, 0, 80, 2), root.Children[0].Layout);
        Assert.Equal(new Rect(0, 2, 80, 21), root.Children[1].Layout);
        Assert.Equal(new Rect(0, 23, 80, 1), root.Children[2].Layout);
    }

    [Fact]
    public void Min_height_holds_a_column_item_open()
    {
        var root = new BoxNode(Column, Leaf(5, 1, new Style { MinHeight = 4 }), Leaf(5, 1));
        Lay(root);
        Assert.Equal(4, root.Children[0].Layout.Height);
        Assert.Equal(4, root.Children[1].Layout.Y);
    }

    [Fact]
    public void Max_height_caps_a_column_item()
    {
        var root = new BoxNode(Column, Leaf(5, 6, new Style { MaxHeight = 2 }), Leaf(5, 1));
        Lay(root);
        Assert.Equal(2, root.Children[0].Layout.Height);
        Assert.Equal(2, root.Children[1].Layout.Y);
    }

    [Fact]
    public void Cross_axis_min_and_max_clamp_a_stretched_item()
    {
        var root = new BoxNode(Row, Leaf(5, 1, new Style { MaxHeight = 4 }), Leaf(5, 1, new Style { MinHeight = 12 }));
        Lay(root, 40, 10);
        Assert.Equal(4, root.Children[0].Layout.Height);
        Assert.Equal(12, root.Children[1].Layout.Height);
    }

    [Fact]
    public void Overflow_does_not_change_layout()
    {
        var hidden = new BoxNode(Row with { Overflow = Overflow.Hidden }, Leaf(60, 1, new Style { FlexShrink = 0 }));
        var visible = new BoxNode(Row with { Overflow = Overflow.Visible }, Leaf(60, 1, new Style { FlexShrink = 0 }));
        Lay(hidden, 40, 5);
        Lay(visible, 40, 5);
        Assert.Equal(hidden.Children[0].Layout, visible.Children[0].Layout);
        Assert.Equal(60, hidden.Children[0].Layout.Width);
    }

    [Fact]
    public void Content_that_overflows_a_column_keeps_flowing_past_the_bottom()
    {
        var root = new BoxNode(Column,
            Leaf(5, 8, new Style { FlexShrink = 0 }),
            Leaf(5, 8, new Style { FlexShrink = 0 }));
        Lay(root, 40, 10);
        Assert.Equal(8, root.Children[1].Layout.Y);
        Assert.Equal(8, root.Children[1].Layout.Height);
    }

    [Fact]
    public void An_empty_container_lays_out_without_error()
    {
        var root = new BoxNode(Row);
        Lay(root, 0, 0);
        Assert.Equal(Rect.Empty, root.Layout);
    }

    [Fact]
    public void Measure_of_a_leaf_is_clamped_by_its_explicit_and_min_max_sizes()
    {
        var leaf = Leaf(50, 5, new Style { MaxWidth = 20, MinHeight = 8 });
        Assert.Equal(new Size(20, 8), FlexLayout.Measure(leaf, 100, 100));
        var fixedLeaf = Leaf(50, 5, new Style { Width = 7, Height = 2 });
        Assert.Equal(new Size(7, 2), FlexLayout.Measure(fixedLeaf, null, null));
    }

    [Fact]
    public void Measure_of_a_box_includes_its_inset()
    {
        var box = new BoxNode(Row with { Padding = new Edges(1), Border = BorderStyle.Single }, Leaf(6, 2));
        Assert.Equal(new Size(10, 6), FlexLayout.Measure(box, null, null));
    }

    [Fact]
    public void A_sibling_of_a_changed_leaf_is_not_measured_again()
    {
        var calls = new int[2];
        var a = new TextLeafNode((w, _) => { calls[0]++; return new Size(Math.Min(w ?? 10, 10), 1); });
        var b = new TextLeafNode((w, _) => { calls[1]++; return new Size(Math.Min(w ?? 10, 10), 1); });
        var root = new BoxNode(Column, a, b);

        Lay(root, 40, 10);
        var afterFirst = (calls[0], calls[1]);
        Assert.True(afterFirst.Item1 > 0 && afterFirst.Item2 > 0);

        a.InvalidateLayout();
        Lay(root, 40, 10);

        Assert.True(calls[0] > afterFirst.Item1, "the changed leaf must be re-measured");
        Assert.Equal(afterFirst.Item2, calls[1]);
    }

    [Fact]
    public void A_clean_tree_laid_out_again_measures_nothing()
    {
        var calls = 0;
        var leaf = new TextLeafNode((w, _) => { calls++; return new Size(Math.Min(w ?? 10, 10), 1); });
        var inner = new BoxNode(Column with { FlexGrow = 1 }, leaf);
        var root = new BoxNode(Row, inner, Leaf(5, 1));

        Lay(root, 40, 10);
        var after = calls;
        Lay(root, 40, 10);
        Lay(root, 40, 10);

        Assert.Equal(after, calls);
    }

    [Fact]
    public void Changing_a_style_invalidates_and_relays()
    {
        var leaf = Leaf(5, 1);
        var root = new BoxNode(Row, leaf);
        Lay(root, 40, 10);
        Assert.Equal(5, leaf.Layout.Width);

        leaf.Style = leaf.Style with { Width = 9 };
        Lay(root, 40, 10);
        Assert.Equal(9, leaf.Layout.Width);
    }

    [Fact]
    public void A_resize_relays_a_clean_tree_to_the_new_viewport()
    {
        var root = new BoxNode(Row, Leaf(0, 1, new Style { FlexGrow = 1 }));
        Lay(root, 40, 10);
        Lay(root, 60, 5);
        Assert.Equal(new Rect(0, 0, 60, 5), root.Children[0].Layout);
    }

    [Fact]
    public void Rects_are_absolute_at_every_depth()
    {
        var leaf = Leaf(3, 1);
        var inner = new BoxNode(Column with { Padding = new Edges(2) }, leaf);
        var root = new BoxNode(Row with { Padding = new Edges(1) }, inner);
        Lay(root, 40, 10);
        Assert.Equal(new Rect(1, 1, 7, 8), inner.Layout);
        Assert.Equal(new Rect(3, 3, 3, 1), leaf.Layout);
    }

    [Fact]
    public void Overflowing_content_with_justify_flex_end_keeps_the_end_in_view()
    {
        // Three items of height 2, shrink 0, in a column of height 3: the last
        // item ends at the container's bottom and the first overflows the top.
        var item = new Style { FlexShrink = 0 };
        var column = new BoxNode(new Style { FlexDirection = FlexDirection.Column, JustifyContent = JustifyContent.FlexEnd },
            new TextLeafNode(5, 2, item), new TextLeafNode(5, 2, item), new TextLeafNode(5, 2, item));

        FlexLayout.Layout(column, new Size(10, 3));

        Assert.Equal(-3, column.Children[0].Layout.Y);
        Assert.Equal(-1, column.Children[1].Layout.Y);
        Assert.Equal(1, column.Children[2].Layout.Y);
        Assert.Equal(3, column.Children[2].Layout.Bottom);
    }

    [Fact]
    public void Overflowing_content_with_justify_center_overflows_both_ways()
    {
        var item = new Style { FlexShrink = 0 };
        var column = new BoxNode(new Style { FlexDirection = FlexDirection.Column, JustifyContent = JustifyContent.Center },
            new TextLeafNode(5, 4, item), new TextLeafNode(5, 4, item));

        FlexLayout.Layout(column, new Size(10, 4));

        Assert.Equal(-2, column.Children[0].Layout.Y);
        Assert.Equal(2, column.Children[1].Layout.Y);
    }
}
