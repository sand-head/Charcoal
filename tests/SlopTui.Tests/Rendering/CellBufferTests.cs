using SlopTui.Layout;
using SlopTui.Rendering;

namespace SlopTui.Tests.Rendering;

public class CellBufferTests
{
    private static readonly Color Fg = Color.Default;
    private static readonly Color Bg = Color.Default;

    [Fact]
    public void A_new_buffer_is_blank()
    {
        var buffer = new CellBuffer(3, 2);
        Assert.All(Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(0, 2).Select(y => buffer[x, y])),
            cell => Assert.Equal(Cell.Blank, cell));
        Assert.Equal("\n", buffer.ToString());
    }

    [Fact]
    public void Put_writes_a_cell_and_advances_by_its_width()
    {
        var buffer = new CellBuffer(4, 1);
        Assert.Equal(1, buffer.Put(0, 0, "a", 1, Fg, Bg, TextStyle.None));
        Assert.Equal("a", buffer.RowText(0).TrimEnd());
    }

    [Fact]
    public void Put_outside_the_clip_is_dropped()
    {
        var buffer = new CellBuffer(4, 2);
        buffer.PushClip(new Rect(0, 0, 2, 1));
        Assert.Equal(0, buffer.Put(2, 0, "x", 1, Fg, Bg, TextStyle.None));
        Assert.Equal(0, buffer.Put(0, 1, "y", 1, Fg, Bg, TextStyle.None));
        Assert.Equal(1, buffer.Put(1, 0, "z", 1, Fg, Bg, TextStyle.None));
        Assert.Equal(" z", buffer.RowText(0).TrimEnd());
    }

    [Fact]
    public void A_wide_cluster_that_does_not_fit_the_clip_is_not_drawn_at_all()
    {
        var buffer = new CellBuffer(4, 1);
        buffer.PushClip(new Rect(0, 0, 3, 1));
        Assert.Equal(0, buffer.Put(2, 0, "字", 2, Fg, Bg, TextStyle.None));
        Assert.Equal("", buffer.RowText(0).TrimEnd());
    }

    [Fact]
    public void A_wide_cluster_owns_a_continuation_cell()
    {
        var buffer = new CellBuffer(4, 1);
        buffer.Put(1, 0, "字", 2, Fg, Bg, TextStyle.None);
        Assert.Equal(2, buffer[1, 0].Width);
        Assert.True(buffer[2, 0].IsContinuation);
        Assert.Equal(" 字 ", buffer.RowText(0));
    }

    [Fact]
    public void Overwriting_the_left_half_of_a_wide_cluster_blanks_the_right_half()
    {
        var buffer = new CellBuffer(4, 1);
        buffer.Put(1, 0, "字", 2, Fg, Bg, TextStyle.None);
        buffer.Put(1, 0, "a", 1, Fg, Bg, TextStyle.None);
        Assert.Equal("a", buffer[1, 0].Cluster);
        Assert.Equal(Cell.Blank, buffer[2, 0]);
    }

    [Fact]
    public void Overwriting_the_right_half_of_a_wide_cluster_blanks_the_left_half()
    {
        var buffer = new CellBuffer(4, 1);
        buffer.Put(1, 0, "字", 2, Fg, Bg, TextStyle.None);
        buffer.Put(2, 0, "b", 1, Fg, Bg, TextStyle.None);
        Assert.Equal(Cell.Blank, buffer[1, 0]);
        Assert.Equal("b", buffer[2, 0].Cluster);
    }

    [Fact]
    public void PutText_measures_clusters_and_stops_at_the_clip()
    {
        var buffer = new CellBuffer(5, 1);
        buffer.PushClip(new Rect(0, 0, 4, 1));
        var used = buffer.PutText(0, 0, "ab字cd", Fg, Bg, TextStyle.None);
        Assert.Equal(4, used);
        Assert.Equal("ab字", buffer.RowText(0).TrimEnd());
    }

    [Fact]
    public void RowHash_changes_when_the_row_changes_and_not_otherwise()
    {
        var buffer = new CellBuffer(4, 2);
        var before = buffer.RowHash(0);
        var other = buffer.RowHash(1);
        buffer.Put(0, 0, "a", 1, Fg, Bg, TextStyle.None);
        Assert.NotEqual(before, buffer.RowHash(0));
        Assert.Equal(other, buffer.RowHash(1));
    }

    [Fact]
    public void Nested_clips_only_shrink_and_pop_restores()
    {
        var buffer = new CellBuffer(10, 10);
        buffer.PushClip(new Rect(2, 2, 6, 6));
        buffer.PushClip(new Rect(0, 0, 4, 4));
        Assert.Equal(new Rect(2, 2, 2, 2), buffer.Clip);
        buffer.PopClip();
        Assert.Equal(new Rect(2, 2, 6, 6), buffer.Clip);
        buffer.PopClip();
        Assert.Equal(new Rect(0, 0, 10, 10), buffer.Clip);
    }

    [Fact]
    public void Fill_respects_the_clip()
    {
        var buffer = new CellBuffer(4, 2);
        buffer.PushClip(new Rect(1, 0, 2, 2));
        buffer.Fill(new Rect(0, 0, 4, 2), Cell.Space(Fg, Color.Red));
        Assert.Equal(Color.Default, buffer[0, 0].Background);
        Assert.Equal(Color.Red, buffer[1, 0].Background);
        Assert.Equal(Color.Red, buffer[2, 1].Background);
        Assert.Equal(Color.Default, buffer[3, 1].Background);
    }

    [Fact]
    public void Resize_blanks_everything_and_resets_the_clip()
    {
        var buffer = new CellBuffer(2, 2);
        buffer.Put(0, 0, "a", 1, Fg, Bg, TextStyle.None);
        buffer.PushClip(new Rect(0, 0, 1, 1));
        buffer.Resize(3, 1);
        Assert.Equal(new Rect(0, 0, 3, 1), buffer.Clip);
        Assert.Equal(Cell.Blank, buffer[0, 0]);
    }

    [Fact]
    public void CopyFrom_makes_the_buffers_equal()
    {
        var a = new CellBuffer(3, 1);
        var b = new CellBuffer(3, 1);
        a.Put(1, 0, "x", 1, Fg, Bg, TextStyle.Bold);
        b.CopyFrom(a);
        Assert.Equal(a[1, 0], b[1, 0]);
        Assert.Equal(a.RowHash(0), b.RowHash(0));
    }
}
