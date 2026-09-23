using Charcoal.Layout;

namespace Charcoal.Tests.Layout;

public class GridLayoutTests
{
    private static TextLeafNode Leaf(int width, int height, Style? style = null) => new(width, height, style);

    private static Style Grid(params Track[] columns) =>
        new() { Display = Display.Grid, GridTemplateColumns = new TrackList(columns) };

    [Fact]
    public void Auto_placement_fills_columns_then_rows()
    {
        var root = new BoxNode(Grid(Track.Cells(5), Track.Cells(5)), Leaf(2, 1), Leaf(2, 1), Leaf(2, 1));
        FlexLayout.Layout(root, new Size(10, 2));

        Assert.Equal(new Rect(0, 0, 5, 1), root.Children[0].Layout);
        Assert.Equal(new Rect(5, 0, 5, 1), root.Children[1].Layout);
        Assert.Equal(new Rect(0, 1, 5, 1), root.Children[2].Layout);
        Assert.Equal(new Size(10, 2), root.ContentSize);
    }

    [Fact]
    public void Fraction_tracks_share_the_remaining_width_exactly()
    {
        var root = new BoxNode(Grid(Track.Fr(1), Track.Fr(2)), Leaf(1, 1), Leaf(1, 1));
        FlexLayout.Layout(root, new Size(10, 1));

        Assert.Equal(3, root.Children[0].Layout.Width);
        Assert.Equal(7, root.Children[1].Layout.Width);
        Assert.Equal(3, root.Children[1].Layout.X);
    }

    [Fact]
    public void Fixed_percent_and_fraction_tracks_mix()
    {
        var root = new BoxNode(Grid(Track.Cells(4), Track.Percent(50), Track.Fr(1)), Leaf(1, 1), Leaf(1, 1), Leaf(1, 1));
        FlexLayout.Layout(root, new Size(20, 1));

        Assert.Equal([0, 4, 14], root.Children.Select(c => c.Layout.X));
        Assert.Equal([4, 10, 6], root.Children.Select(c => c.Layout.Width));
    }

    [Fact]
    public void Auto_tracks_take_their_widest_item()
    {
        var root = new BoxNode(Grid(Track.Auto, Track.Fr(1)), Leaf(6, 1), Leaf(1, 1), Leaf(3, 1), Leaf(1, 1));
        FlexLayout.Layout(root, new Size(20, 2));

        Assert.Equal(6, root.Children[0].Layout.Width);
        Assert.Equal(6, root.Children[2].Layout.Width);   // stretched to the track
        Assert.Equal(14, root.Children[1].Layout.Width);
    }

    [Fact]
    public void Explicit_placement_and_a_span()
    {
        var a = Leaf(1, 1, new Style { GridColumnStart = 2, GridColumnSpan = 2 });
        var b = Leaf(1, 1);
        var root = new BoxNode(Grid(Track.Cells(3), Track.Cells(3), Track.Cells(3)) with { ColumnGap = 1 }, a, b);
        FlexLayout.Layout(root, new Size(11, 1));

        Assert.Equal(new Rect(4, 0, 7, 1), a.Layout);
        // Sparse auto-placement: the cursor sits after the placed item, and
        // with no room left in row 0 the next item starts row 1, as CSS does —
        // the hole before the spanning item is not filled.
        Assert.Equal(new Rect(0, 1, 3, 1), b.Layout);
    }

    [Fact]
    public void A_row_span_grows_the_auto_rows_it_covers()
    {
        var tall = Leaf(3, 4, new Style { GridRowSpan = 2 });
        var next = Leaf(3, 1);
        var root = new BoxNode(Grid(Track.Cells(4)), tall, next);
        FlexLayout.Layout(root, new Size(4, 5));

        Assert.Equal(new Rect(0, 0, 4, 4), tall.Layout);
        Assert.Equal(new Rect(0, 4, 4, 1), next.Layout);
    }

    [Fact]
    public void Items_are_placed_in_their_cell_by_justify_and_align()
    {
        var item = Leaf(4, 1);
        var style = Grid(Track.Cells(10)) with
        {
            GridTemplateRows = new TrackList([Track.Cells(5)]),
            JustifyItems = AlignItems.Center,
            AlignItems = AlignItems.FlexEnd,
        };
        var root = new BoxNode(style, item);
        FlexLayout.Layout(root, new Size(10, 5));

        Assert.Equal(new Rect(3, 4, 4, 1), item.Layout);
    }

    [Fact]
    public void Align_self_overrides_the_grid_alignment()
    {
        var item = Leaf(4, 1, new Style { AlignSelf = AlignSelf.Center });
        var style = Grid(Track.Cells(10)) with { GridTemplateRows = new TrackList([Track.Cells(5)]), AlignItems = AlignItems.FlexEnd };
        var root = new BoxNode(style, item);
        FlexLayout.Layout(root, new Size(10, 5));

        Assert.Equal(2, item.Layout.Y);
        Assert.Equal(10, item.Layout.Width);   // justify-items stretch
    }

    [Fact]
    public void Align_content_stretch_grows_the_auto_rows()
    {
        var root = new BoxNode(Grid(Track.Cells(4)), Leaf(4, 1), Leaf(4, 1));
        FlexLayout.Layout(root, new Size(4, 6));

        Assert.Equal(new Rect(0, 0, 4, 3), root.Children[0].Layout);
        Assert.Equal(new Rect(0, 3, 4, 3), root.Children[1].Layout);
    }

    [Fact]
    public void Align_content_flex_end_places_the_rows_at_the_bottom()
    {
        var root = new BoxNode(Grid(Track.Cells(4)) with { AlignContent = AlignContent.FlexEnd }, Leaf(4, 1), Leaf(4, 1));
        FlexLayout.Layout(root, new Size(4, 6));

        Assert.Equal(4, root.Children[0].Layout.Y);
        Assert.Equal(5, root.Children[1].Layout.Y);
    }

    [Fact]
    public void Percent_tracks_resolve_against_the_content_box()
    {
        var root = new BoxNode(Grid(Track.Percent(25), Track.Percent(75)) with { Padding = new Edges(0, 2) }, Leaf(1, 1), Leaf(1, 1));
        FlexLayout.Layout(root, new Size(24, 1));

        Assert.Equal(5, root.Children[0].Layout.Width);
        Assert.Equal(15, root.Children[1].Layout.Width);
        Assert.Equal(7, root.Children[1].Layout.X);
    }

    [Fact]
    public void Gaps_separate_tracks_on_both_axes()
    {
        var style = Grid(Track.Cells(3), Track.Cells(3)) with
        {
            GridTemplateRows = new TrackList([Track.Cells(1), Track.Cells(1)]),
            RowGap = 1,
            ColumnGap = 1,
        };
        var root = new BoxNode(style, Leaf(1, 1), Leaf(1, 1), Leaf(1, 1), Leaf(1, 1));
        FlexLayout.Layout(root, new Size(7, 3));

        Assert.Equal(new Rect(4, 2, 3, 1), root.Children[3].Layout);
        Assert.Equal(new Size(7, 3), root.ContentSize);
    }

    [Fact]
    public void An_item_that_names_a_row_takes_the_first_free_column_in_it()
    {
        var a = Leaf(1, 1);
        var b = Leaf(1, 1, new Style { GridRowStart = 1 });
        var root = new BoxNode(Grid(Track.Cells(3), Track.Cells(3)), a, b);
        FlexLayout.Layout(root, new Size(6, 1));

        Assert.Equal(0, a.Layout.X);
        Assert.Equal(3, b.Layout.X);
        Assert.Equal(0, b.Layout.Y);
    }

    [Fact]
    public void An_item_that_names_a_column_takes_the_first_row_where_it_is_free()
    {
        var a = Leaf(1, 1);
        var b = Leaf(1, 1, new Style { GridColumnStart = 1 });
        var root = new BoxNode(Grid(Track.Cells(3), Track.Cells(3)), a, b);
        FlexLayout.Layout(root, new Size(6, 2));

        Assert.Equal(new Rect(0, 0, 3, 1), a.Layout);
        Assert.Equal(new Rect(0, 1, 3, 1), b.Layout);
    }

    [Fact]
    public void Placing_past_the_explicit_rows_makes_implicit_ones()
    {
        var far = Leaf(1, 1, new Style { GridRowStart = 4 });
        var root = new BoxNode(Grid(Track.Cells(3)), far);
        FlexLayout.Layout(root, new Size(3, 4));

        Assert.Equal(3, far.Layout.Y);
    }

    [Fact]
    public void Measuring_a_grid_sums_its_tracks()
    {
        var grid = new BoxNode(Grid(Track.Cells(4), Track.Cells(6)) with { ColumnGap = 2 }, Leaf(2, 1), Leaf(2, 1));
        Assert.Equal(new Size(12, 1), FlexLayout.Measure(grid, 20, null));
    }

    [Fact]
    public void Unbounded_fraction_tracks_size_like_auto()
    {
        var grid = new BoxNode(Grid(Track.Fr(1), Track.Fr(1)), Leaf(3, 1), Leaf(5, 1));
        Assert.Equal(new Size(8, 1), FlexLayout.Measure(grid, null, null));
    }

    [Fact]
    public void A_grid_in_a_row_has_a_min_content_width_of_its_columns()
    {
        var grid = new BoxNode(Grid(Track.Cells(4), Track.Auto) with { ColumnGap = 1 }, Leaf(1, 1), Leaf(3, 1));
        Assert.Equal(5, FlexLayout.MinContentWidth(grid));   // 4 + gap + a leaf's own 0
    }

    [Fact]
    public void A_scrolling_grid_shifts_its_rows()
    {
        var root = new BoxNode(Grid(Track.Cells(4)) with { Overflow = Overflow.Scroll, AlignContent = AlignContent.FlexStart }) { ScrollTop = 2 };
        for (var i = 0; i < 6; i++) root.Add(Leaf(4, 1));
        FlexLayout.Layout(root, new Size(4, 3));

        Assert.Equal(-2, root.Children[0].Layout.Y);
        Assert.Equal(0, root.Children[2].Layout.Y);
        Assert.Equal(6, root.ContentSize.Height);
    }
}
