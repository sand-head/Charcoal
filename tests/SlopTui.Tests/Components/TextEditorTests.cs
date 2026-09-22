using SlopTui.Components;

namespace SlopTui.Tests.Components;

public class TextEditorTests
{
    private static TextEditor Editor(string text, bool multiline = false)
    {
        var editor = new TextEditor { Multiline = multiline };
        editor.SetText(text);
        return editor;
    }

    [Fact]
    public void Typing_inserts_at_the_caret_and_backspace_takes_a_whole_cluster()
    {
        var editor = Editor("ab");
        editor.MoveLeft();
        editor.Insert("👍🏽");
        Assert.Equal("a👍🏽b", editor.Text);
        Assert.Equal(5, editor.Caret);   // after the four UTF-16 units of the emoji

        editor.Backspace();
        Assert.Equal("ab", editor.Text);
        Assert.Equal(1, editor.Caret);

        editor.Insert("é");   // e + combining acute
        editor.MoveLeft();
        Assert.Equal(1, editor.Caret);   // the cluster is one step
        editor.Delete();
        Assert.Equal("ab", editor.Text);
    }

    [Fact]
    public void Word_moves_and_kills_are_readlines()
    {
        var editor = Editor("one two  three");
        editor.MoveLeft(byWord: true);
        Assert.Equal(9, editor.Caret);
        editor.MoveLeft(byWord: true);
        Assert.Equal(4, editor.Caret);
        editor.MoveRight(byWord: true);
        Assert.Equal(7, editor.Caret);

        editor.MoveToLineEnd();
        editor.DeleteWordBackward();
        Assert.Equal("one two  ", editor.Text);
        editor.DeleteWordBackward();
        Assert.Equal("one ", editor.Text);

        editor.MoveToLineStart();
        editor.DeleteWordForward();
        Assert.Equal(" ", editor.Text);
    }

    [Fact]
    public void Kills_to_the_ends_of_the_line_stop_at_newlines()
    {
        var editor = Editor("first\nsecond line\nthird", multiline: true);
        editor.MoveTo(9);   // "sec|ond line"
        editor.DeleteToLineStart();
        Assert.Equal("first\nond line\nthird", editor.Text);
        editor.DeleteToLineEnd();
        Assert.Equal("first\n\nthird", editor.Text);
        // On an empty line the kill takes the newline, as readline does.
        editor.DeleteToLineEnd();
        Assert.Equal("first\nthird", editor.Text);
    }

    [Fact]
    public void A_single_line_editor_flattens_newlines_and_a_maxlength_caps_the_code_points()
    {
        var editor = Editor("");
        editor.Insert("two\r\nlines");
        Assert.Equal("two lines", editor.Text);

        editor.MaxLength = 11;
        editor.Insert("👍👍👍");
        Assert.Equal("two lines👍👍", editor.Text);
        Assert.False(editor.Insert("x"));
    }

    [Fact]
    public void Setting_the_text_moves_the_caret_to_the_end_only_when_it_changed()
    {
        var editor = Editor("hello");
        editor.MoveToLineStart();
        Assert.False(editor.SetText("hello"));
        Assert.Equal(0, editor.Caret);
        Assert.True(editor.SetText("hello there"));
        Assert.Equal(11, editor.Caret);
    }

    [Fact]
    public void Lines_wrap_after_a_space_or_inside_a_long_word_and_break_at_newlines()
    {
        var editor = Editor("the quick brown\nfox jumpsoverthelazydog", multiline: true);
        var lines = editor.Lines(10, wrap: true);
        Assert.Equal(
            [("the quick ", false), ("brown", true), ("fox ", false), ("jumpsovert", false), ("helazydog", true)],
            lines.Select(l => (editor.Text[l.Start..l.End], l.Hard)));

        // Without wrapping a line is a line.
        Assert.Equal(2, editor.Lines(10, wrap: false).Count);
    }

    [Fact]
    public void The_caret_after_a_soft_wrap_is_at_the_start_of_the_next_line_and_vertical_moves_keep_their_column()
    {
        var editor = Editor("the quick brown\nfox\nlonger line here", multiline: true);
        editor.MoveTo(10);   // after "the quick ", which is where the wrap happens
        Assert.Equal((0, 1), editor.CaretPosition(10, true));
        editor.MoveTo(9);
        Assert.Equal((9, 0), editor.CaretPosition(10, true));

        editor.MoveTo(4);    // "the |quick"
        Assert.True(editor.MoveVertical(1, 10, true));
        Assert.Equal((4, 1), editor.CaretPosition(10, true));   // "brow|n"
        Assert.True(editor.MoveVertical(1, 10, true));
        Assert.Equal((3, 2), editor.CaretPosition(10, true));   // "fox|", shorter than the goal
        Assert.True(editor.MoveVertical(1, 10, true));
        Assert.Equal((4, 3), editor.CaretPosition(10, true));   // the goal column comes back
        Assert.True(editor.MoveVertical(1, 10, true));
        Assert.Equal((4, 4), editor.CaretPosition(10, true));   // "here" wrapped
        Assert.False(editor.MoveVertical(1, 10, true));         // the last line

        Assert.Equal(6, editor.IndexAt(10, true, 6, 0));
        Assert.Equal(10, editor.IndexAt(10, true, 40, 0));   // past the end of a line is its end, the wrapped space included
    }

    [Fact]
    public void Masked_text_is_one_column_a_cluster_and_shows_as_bullets()
    {
        var editor = new TextEditor { Masked = true };
        editor.SetText("日本👍");
        Assert.Equal(3, editor.Width(editor.Text));
        Assert.Equal("•••", editor.Display(editor.Text));
        Assert.Equal((3, 0), editor.CaretPosition(10, false));
    }
}
