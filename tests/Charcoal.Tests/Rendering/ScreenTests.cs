using Charcoal.Rendering;

namespace Charcoal.Tests.Rendering;

public class ScreenTests
{
    private const string Esc = "\e";
    private static readonly Color D = Color.Default;

    private static Screen Blank(int width, int height)
    {
        var screen = new Screen(width, height);
        screen.Flush();   // establish a blank shown screen
        return screen;
    }

    [Fact]
    public void The_first_flush_paints_every_row()
    {
        var screen = new Screen(4, 3);
        var frame = screen.Flush();
        // Nothing is known, so every row is addressed; blank rows cost one erase each.
        Assert.Contains(Esc + "[1;1H", frame);
        Assert.Contains(Esc + "[2;1H", frame);
        Assert.Contains(Esc + "[3;1H", frame);
        Assert.Equal(3, frame.Split(Esc + "[K").Length - 1);
    }

    [Fact]
    public void A_flush_with_nothing_changed_is_empty()
    {
        var screen = Blank(4, 2);
        Assert.Equal("", screen.Flush());
    }

    [Fact]
    public void One_changed_cell_emits_one_position_and_one_pen()
    {
        var screen = Blank(6, 2);
        screen.Back.Put(2, 1, "x", 1, D, D, TextStyle.None);
        var frame = screen.Flush();
        Assert.Equal(Esc + "[?25l" + Esc + "[2;3H" + Esc + "[0;39;49m" + "x" + Esc + "[0m", frame);
    }

    [Fact]
    public void Only_the_span_between_the_first_and_last_difference_is_repainted()
    {
        var screen = Blank(10, 1);
        screen.Back.PutText(0, 0, "abcdefghij", D, D, TextStyle.None);
        screen.Flush();
        screen.Back.Put(2, 0, "X", 1, D, D, TextStyle.None);
        screen.Back.Put(5, 0, "Y", 1, D, D, TextStyle.None);
        var frame = screen.Flush();
        Assert.Contains(Esc + "[1;3H" + Esc + "[0;39;49mXdeY", frame);
        Assert.DoesNotContain("ab", frame);
        Assert.DoesNotContain("ghij", frame);
    }

    [Fact]
    public void A_blanked_tail_is_erased_not_spaced()
    {
        var screen = Blank(10, 1);
        screen.Back.PutText(0, 0, "abcdefghij", D, D, TextStyle.None);
        screen.Flush();
        screen.Back.Fill(new global::Charcoal.Layout.Rect(3, 0, 7, 1), Cell.Blank);
        var frame = screen.Flush();
        Assert.Contains(Esc + "[1;4H", frame);
        Assert.Contains(Esc + "[K", frame);
        Assert.DoesNotContain("       ", frame);
    }

    [Fact]
    public void A_tail_with_a_background_is_content_not_blank()
    {
        var screen = Blank(4, 1);
        screen.Back.Fill(new global::Charcoal.Layout.Rect(0, 0, 4, 1), Cell.Space(D, Color.Red));
        var frame = screen.Flush();
        Assert.DoesNotContain(Esc + "[K", frame);
        Assert.Contains("    ", frame);
    }

    [Fact]
    public void A_repaint_never_starts_on_a_continuation_cell()
    {
        var screen = Blank(6, 1);
        screen.Back.Put(1, 0, "字", 2, D, D, TextStyle.None);
        screen.Back.Put(3, 0, "a", 1, D, D, TextStyle.None);
        screen.Flush();
        // Change only what follows the wide glyph's right half: the diff still
        // begins at the glyph itself when the continuation is where it differs.
        screen.Back.Put(3, 0, "b", 1, D, D, TextStyle.None);
        var frame = screen.Flush();
        Assert.Contains(Esc + "[1;4H", frame);

        // Now force the continuation to differ: re-put the glyph in another colour.
        screen.Back.Put(1, 0, "字", 2, Color.Red, D, TextStyle.None);
        frame = screen.Flush();
        Assert.Contains(Esc + "[1;2H", frame);
        Assert.Contains("字", frame);
    }

    [Fact]
    public void Runs_of_equal_attributes_share_one_pen()
    {
        var screen = Blank(6, 1);
        screen.Back.PutText(0, 0, "abc", Color.Red, D, TextStyle.None);
        screen.Back.PutText(3, 0, "def", Color.Blue, D, TextStyle.None);
        var frame = screen.Flush();
        Assert.Equal(2, frame.Split(Esc + "[0;").Length - 1 + frame.Split(Esc + "[34m").Length - 1);
        Assert.Contains(Esc + "[0;31;49mabc" + Esc + "[34mdef", frame);
    }

    [Fact]
    public void Adding_a_style_flag_is_incremental_and_dropping_one_resets()
    {
        var screen = Blank(4, 1);
        screen.Back.Put(0, 0, "a", 1, D, D, TextStyle.None);
        screen.Back.Put(1, 0, "b", 1, D, D, TextStyle.Bold);
        screen.Back.Put(2, 0, "c", 1, D, D, TextStyle.None);
        var frame = screen.Flush();
        Assert.Contains(Esc + "[0;39;49ma" + Esc + "[1mb" + Esc + "[0;39;49mc", frame);
    }

    [Fact]
    public void Colour_and_style_change_together_is_one_sequence()
    {
        var screen = Blank(4, 1);
        screen.Back.Put(0, 0, "a", 1, D, D, TextStyle.None);
        screen.Back.Put(1, 0, "b", 1, Color.Rgb(1, 2, 3), D, TextStyle.Underline);
        var frame = screen.Flush();
        Assert.Contains(Esc + "[38;2;1;2;3;4mb", frame);
    }

    [Fact]
    public void Synchronized_output_wraps_the_frame()
    {
        var screen = Blank(4, 1);
        screen.SynchronizedOutput = true;
        screen.Back.Put(0, 0, "a", 1, D, D, TextStyle.None);
        var frame = screen.Flush();
        Assert.StartsWith(Esc + "[?2026h", frame);
        Assert.EndsWith(Esc + "[?2026l", frame);
    }

    [Fact]
    public void A_visible_cursor_is_positioned_and_shown_after_the_paint()
    {
        var screen = Blank(4, 2);
        screen.Cursor = (2, 1);
        screen.CursorVisible = true;
        screen.Back.Put(0, 0, "a", 1, D, D, TextStyle.None);
        var frame = screen.Flush();
        Assert.EndsWith(Esc + "[0m" + Esc + "[2;3H" + Esc + "[?25h", frame);
        Assert.StartsWith(Esc + "[?25l", frame);
    }

    [Fact]
    public void A_cursor_move_alone_is_a_frame()
    {
        var screen = Blank(4, 2);
        screen.Cursor = (0, 0);
        screen.CursorVisible = true;
        screen.Flush();
        screen.Cursor = (1, 0);
        Assert.Equal(Esc + "[1;2H" + Esc + "[?25h", screen.Flush());
        Assert.Equal("", screen.Flush());
    }

    [Fact]
    public void Hiding_the_cursor_alone_is_a_frame()
    {
        var screen = Blank(4, 2);
        screen.Cursor = (0, 0);
        screen.CursorVisible = true;
        screen.Flush();
        screen.CursorVisible = false;
        Assert.Equal(Esc + "[?25l", screen.Flush());
    }

    [Fact]
    public void Invalidate_repaints_everything_next_flush()
    {
        var screen = Blank(3, 2);
        screen.Back.Put(0, 0, "a", 1, D, D, TextStyle.None);
        screen.Flush();
        screen.Invalidate();
        var frame = screen.Flush();
        Assert.Contains(Esc + "[1;1H", frame);
        Assert.Contains(Esc + "[2;1H", frame);
        Assert.Contains("a", frame);
    }

    [Fact]
    public void Resize_gives_a_blank_back_buffer_of_the_new_size_and_repaints_all()
    {
        var screen = Blank(3, 2);
        screen.Back.Put(0, 0, "a", 1, D, D, TextStyle.None);
        screen.Resize(5, 1);
        Assert.Equal(5, screen.Width);
        Assert.Equal(1, screen.Height);
        Assert.Equal(Cell.Blank, screen.Back[0, 0]);
        Assert.Contains(Esc + "[1;1H", screen.Flush());
    }

    [Fact]
    public void After_a_flush_the_shown_screen_matches_the_back_buffer()
    {
        var screen = Blank(4, 1);
        screen.Back.PutText(0, 0, "abcd", D, D, TextStyle.None);
        screen.Flush();
        screen.Back.PutText(0, 0, "abcd", D, D, TextStyle.None);
        Assert.Equal("", screen.Flush());
    }

    [Fact]
    public void Every_frame_ends_the_pen_in_a_reset()
    {
        var screen = Blank(2, 1);
        screen.Back.Put(0, 0, "a", 1, Color.Red, D, TextStyle.Bold);
        Assert.EndsWith(Esc + "[0m", screen.Flush());
    }
}
