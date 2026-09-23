using Charcoal.Layout;

namespace Charcoal.Tests.Layout;

public class LengthTests
{
    [Fact]
    public void Cells_resolve_to_themselves_whatever_the_parent()
    {
        Assert.Equal(7, Length.Cells(7).Resolve(100));
        Assert.Equal(7, Length.Cells(7).Resolve(null));
    }

    [Fact]
    public void Percent_resolves_against_the_parent_and_rounds()
    {
        Assert.Equal(20, Length.Percent(50).Resolve(40));
        Assert.Equal(13, Length.Percent(33).Resolve(40));
        Assert.Equal(14, Length.Percent(35).Resolve(40));
    }

    [Fact]
    public void Percent_of_an_unknown_parent_is_auto()
    {
        Assert.Null(Length.Percent(50).Resolve(null));
    }

    [Fact]
    public void Auto_resolves_to_nothing()
    {
        Assert.Null(Length.Auto.Resolve(40));
        Assert.True(Length.Auto.IsAuto);
    }

    [Fact]
    public void An_int_converts_to_cells_and_lengths_print_as_css()
    {
        Length length = 5;
        Assert.Equal(Length.Cells(5), length);
        Assert.Equal("5", length.ToString());
        Assert.Equal("50%", Length.Percent(50).ToString());
        Assert.Equal("auto", Length.Auto.ToString());
    }

    [Fact]
    public void Edges_sum_per_axis_and_add()
    {
        var edges = new Edges(1, 2, 3, 4);
        Assert.Equal(6, edges.Horizontal);
        Assert.Equal(4, edges.Vertical);
        Assert.Equal(new Edges(2, 4, 6, 8), edges + edges);
        Assert.Equal(new Edges(1, 2, 1, 2), new Edges(1, 2));
    }

    [Fact]
    public void Rect_deflate_never_goes_negative()
    {
        var rect = new Rect(0, 0, 3, 3);
        Assert.Equal(new Rect(2, 2, 0, 0), rect.Deflate(new Edges(2)));
        Assert.Equal(new Rect(1, 1, 1, 1), rect.Deflate(new Edges(1)));
    }

    [Fact]
    public void Rect_intersect_is_the_overlap_or_empty()
    {
        var a = new Rect(0, 0, 10, 10);
        Assert.Equal(new Rect(5, 5, 5, 5), a.Intersect(new Rect(5, 5, 20, 20)));
        Assert.Equal(Rect.Empty, a.Intersect(new Rect(10, 10, 5, 5)));
        Assert.True(a.Contains(9, 9));
        Assert.False(a.Contains(10, 9));
    }
}
