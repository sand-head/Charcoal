using SlopTui.Terminal;

namespace SlopTui.Tests.Terminal;

public class AnsiTests
{
    [Fact]
    public void Cursor_position_is_one_based_on_the_wire()
    {
        Assert.Equal("\e[1;1H", Ansi.CursorPosition(0, 0));
        Assert.Equal("\e[13;6H", Ansi.CursorPosition(5, 12));
    }

    [Fact]
    public void Mouse_mode_1006_goes_on_last_and_comes_off_first()
    {
        Assert.EndsWith("\e[?1006h", Ansi.MouseOn, StringComparison.Ordinal);
        Assert.StartsWith("\e[?1006l", Ansi.MouseOff, StringComparison.Ordinal);
        Assert.DoesNotContain("1003", Ansi.MouseOn);
        Assert.Contains("1003", Ansi.MouseAnyMotionOn);
    }

    [Fact]
    public void Kitty_push_asks_for_disambiguation_only_and_pop_balances_it()
    {
        Assert.Equal("\e[>1u", Ansi.KittyKeyboardPush);
        Assert.Equal("\e[<u", Ansi.KittyKeyboardPop);
    }

    [Fact]
    public void Synchronized_output_brackets_a_frame()
    {
        Assert.Equal("\e[?2026h", Ansi.SynchronizedOutputBegin);
        Assert.Equal("\e[?2026l", Ansi.SynchronizedOutputEnd);
        Assert.Equal("\e[?2026$p", Ansi.QuerySynchronizedOutput);
    }

    [Fact]
    public void Screen_and_cursor_constants()
    {
        Assert.Equal("\e[?1049h", Ansi.AlternateScreenOn);
        Assert.Equal("\e[?1049l", Ansi.AlternateScreenOff);
        Assert.Equal("\e[?25l", Ansi.HideCursor);
        Assert.Equal("\e[?25h", Ansi.ShowCursor);
        Assert.Equal("\e[?2004h", Ansi.BracketedPasteOn);
        Assert.Equal("\e[?1004h", Ansi.FocusEventsOn);
        Assert.Equal("\e[c", Ansi.QueryDeviceAttributes);
    }
}
