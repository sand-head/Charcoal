using SlopTui.Layout;
using SlopTui.Rendering;

namespace SlopTui.Tests.Rendering;

public class TextLayoutTests
{
    private static TextRun R(string text) => new(text);
    private static TextRun Bold(string text) => new(text, Color.Default, Color.Default, TextStyle.Bold);

    private static string[] Lines(List<List<TextRun>> lines) =>
        lines.Select(l => string.Concat(l.Select(r => r.Text))).ToArray();

    [Fact]
    public void Text_that_fits_is_one_line()
    {
        var lines = TextLayout.Wrap([R("hello world")], 20, TextWrap.Wrap);
        Assert.Equal(["hello world"], Lines(lines));
    }

    [Fact]
    public void Empty_text_is_no_lines()
    {
        Assert.Empty(TextLayout.Wrap([], 10, TextWrap.Wrap));
        Assert.Empty(TextLayout.Wrap([R("")], 10, TextWrap.Wrap));
        Assert.Equal(Size.Empty, TextLayout.Measure([], 10, TextWrap.Wrap));
    }

    [Fact]
    public void Wraps_at_the_last_space_that_fits()
    {
        var lines = TextLayout.Wrap([R("the quick brown fox")], 10, TextWrap.Wrap);
        Assert.Equal(["the quick", "brown fox"], Lines(lines));
    }

    [Fact]
    public void A_space_exactly_at_the_edge_breaks_there()
    {
        var lines = TextLayout.Wrap([R("abcde fghij")], 5, TextWrap.Wrap);
        Assert.Equal(["abcde", "fghij"], Lines(lines));
    }

    [Fact]
    public void Leading_spaces_after_a_soft_break_are_dropped()
    {
        var lines = TextLayout.Wrap([R("abc   def")], 4, TextWrap.Wrap);
        Assert.Equal(["abc", "def"], Lines(lines));
    }

    [Fact]
    public void A_word_longer_than_the_width_is_hard_broken()
    {
        var lines = TextLayout.Wrap([R("abcdefghij")], 4, TextWrap.Wrap);
        Assert.Equal(["abcd", "efgh", "ij"], Lines(lines));
    }

    [Fact]
    public void A_wide_glyph_is_never_split()
    {
        // "字" is two columns; at width 3 only one fits per line beside a letter.
        var lines = TextLayout.Wrap([R("a字b字")], 3, TextWrap.Wrap);
        Assert.Equal(["a字", "b字"], Lines(lines));
    }

    [Fact]
    public void A_wide_glyph_wider_than_the_width_is_dropped_rather_than_looping()
    {
        var lines = TextLayout.Wrap([R("字a")], 1, TextWrap.Wrap);
        Assert.Equal(["a"], Lines(lines));
    }

    [Fact]
    public void Combining_marks_stay_with_their_base()
    {
        // e + combining acute is one cluster of one column.
        var lines = TextLayout.Wrap([R("abécd")], 3, TextWrap.Wrap);
        Assert.Equal(["abé", "cd"], Lines(lines));
    }

    [Fact]
    public void Newlines_break_lines_in_every_mode()
    {
        foreach (var mode in Enum.GetValues<TextWrap>())
        {
            var lines = TextLayout.Wrap([R("a\nb\nc")], 10, mode);
            Assert.Equal(["a", "b", "c"], Lines(lines));
        }
    }

    [Fact]
    public void A_trailing_newline_leaves_an_empty_last_line()
    {
        var lines = TextLayout.Wrap([R("a\n")], 10, TextWrap.Wrap);
        Assert.Equal(["a", ""], Lines(lines));
    }

    [Fact]
    public void A_newline_inside_a_run_keeps_the_style_on_both_sides()
    {
        var lines = TextLayout.Wrap([Bold("a\nb")], 10, TextWrap.Wrap);
        Assert.All(lines, line => Assert.All(line, run => Assert.Equal(TextStyle.Bold, run.Style)));
    }

    [Fact]
    public void Styles_survive_a_soft_break()
    {
        var lines = TextLayout.Wrap([R("one "), Bold("two three")], 7, TextWrap.Wrap);
        Assert.Equal(["one two", "three"], Lines(lines));
        Assert.Equal(TextStyle.Bold, lines[0][1].Style);
        Assert.Equal(TextStyle.Bold, lines[1][0].Style);
    }

    [Fact]
    public void A_run_with_no_space_moves_whole_to_the_next_line_when_the_line_has_content()
    {
        var lines = TextLayout.Wrap([R("ab "), Bold("cdef")], 5, TextWrap.Wrap);
        Assert.Equal(["ab", "cdef"], Lines(lines));
    }

    [Fact]
    public void Clip_keeps_lines_uncut()
    {
        var lines = TextLayout.Wrap([R("abcdefghij")], 4, TextWrap.Clip);
        Assert.Equal(["abcdefghij"], Lines(lines));
    }

    [Fact]
    public void Truncate_cuts_the_end_with_an_ellipsis()
    {
        var lines = TextLayout.Wrap([R("abcdefghij")], 5, TextWrap.Truncate);
        Assert.Equal(["abcd…"], Lines(lines));
        Assert.Equal(5, TextLayout.LineWidth(lines[0]));
    }

    [Fact]
    public void Truncate_start_cuts_the_start_with_an_ellipsis()
    {
        var lines = TextLayout.Wrap([R("abcdefghij")], 5, TextWrap.TruncateStart);
        Assert.Equal(["…ghij"], Lines(lines));
    }

    [Fact]
    public void Truncate_middle_keeps_both_ends()
    {
        var lines = TextLayout.Wrap([R("abcdefghij")], 5, TextWrap.TruncateMiddle);
        Assert.Equal(["ab…ij"], Lines(lines));
    }

    [Fact]
    public void Truncate_leaves_fitting_text_alone()
    {
        var lines = TextLayout.Wrap([R("abc")], 5, TextWrap.Truncate);
        Assert.Equal(["abc"], Lines(lines));
    }

    [Fact]
    public void Truncate_does_not_cut_a_wide_glyph_in_half()
    {
        var lines = TextLayout.Wrap([R("ab字cd")], 4, TextWrap.Truncate);
        // Three columns to keep: "ab" is two, "字" would need two more, so it goes.
        Assert.Equal(["ab…"], Lines(lines));
    }

    [Fact]
    public void Truncate_at_width_one_is_just_the_ellipsis()
    {
        var lines = TextLayout.Wrap([R("abc")], 1, TextWrap.Truncate);
        Assert.Equal(["…"], Lines(lines));
    }

    [Fact]
    public void The_ellipsis_takes_the_style_of_the_run_it_replaced()
    {
        var lines = TextLayout.Wrap([R("ab"), Bold("cdef")], 4, TextWrap.Truncate);
        Assert.Equal(["abc…"], Lines(lines));
        Assert.Equal(TextStyle.Bold, lines[0][^1].Style);
    }

    [Fact]
    public void Measure_bounded_wraps_and_reports_the_widest_line()
    {
        var size = TextLayout.Measure([R("the quick brown fox")], 10, TextWrap.Wrap);
        Assert.Equal(new Size(9, 2), size);
    }

    [Fact]
    public void Measure_unbounded_does_not_wrap()
    {
        var size = TextLayout.Measure([R("the quick brown fox")], null, TextWrap.Wrap);
        Assert.Equal(new Size(19, 1), size);
    }

    [Fact]
    public void Measure_never_reports_wider_than_the_limit_even_when_clipping()
    {
        var size = TextLayout.Measure([R("abcdefghij")], 4, TextWrap.Clip);
        Assert.Equal(new Size(4, 1), size);
    }

    [Fact]
    public void Measure_counts_wide_glyphs_as_two_columns()
    {
        Assert.Equal(new Size(4, 1), TextLayout.Measure([R("字字")], null, TextWrap.Wrap));
    }

    [Fact]
    public void Width_zero_does_not_wrap_forever()
    {
        var lines = TextLayout.Wrap([R("abc")], 0, TextWrap.Wrap);
        Assert.Single(lines);
    }

    private static string Joined(List<TextRun> runs) => string.Concat(runs.Select(r => r.Text));

    [Fact]
    public void Normal_white_space_collapses_runs_of_space_and_drops_them_at_the_edges()
    {
        var runs = new List<TextRun> { R("  Hello \n   "), Bold("world"), R("  \t!  ") };
        TextLayout.CollapseWhitespace(runs, WhiteSpace.Normal);
        Assert.Equal("Hello world !", Joined(runs));
        Assert.Equal(["Hello ", "world", " !"], runs.Select(r => r.Text));
        Assert.Equal(TextStyle.Bold, runs[1].Style);
    }

    [Fact]
    public void A_forced_break_survives_collapsing_and_the_space_around_it_goes()
    {
        var runs = new List<TextRun> { R("a "), TextRun.LineBreak(Color.Default, Color.Default, TextStyle.None), R(" b") };
        TextLayout.CollapseWhitespace(runs, WhiteSpace.Normal);
        Assert.Equal("a\nb", Joined(runs));
        Assert.Equal(2, TextLayout.Wrap(runs, 10, TextWrap.Wrap).Count);
    }

    [Fact]
    public void Pre_line_keeps_newlines_and_collapses_the_rest_and_pre_keeps_everything()
    {
        var runs = new List<TextRun> { R("  a  b \n  c ") };
        TextLayout.CollapseWhitespace(runs, WhiteSpace.PreLine);
        Assert.Equal("a b\nc", Joined(runs));

        runs = [R("  a  b \n  c ")];
        TextLayout.CollapseWhitespace(runs, WhiteSpace.Pre);
        Assert.Equal("  a  b \n  c ", Joined(runs));
    }

    [Fact]
    public void Whitespace_only_text_collapses_to_nothing()
    {
        var runs = new List<TextRun> { R("\n    "), R(" ") };
        TextLayout.CollapseWhitespace(runs, WhiteSpace.NoWrap);
        Assert.Empty(runs);
    }
}
