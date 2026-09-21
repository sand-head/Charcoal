using SlopTui.Input;
using SlopTui.Terminal;

namespace SlopTui.Tests.Input;

public class MouseInputTests
{
    private const string Esc = "\u001B";

    private static MouseEvent Parse(string text)
    {
        Assert.True(MouseInput.TryParse(text, out var mouse, out _, out _));
        return mouse!;
    }

    [Fact]
    public void A_left_press_says_where_it_landed()
    {
        var mouse = Parse($"{Esc}[<0;10;5M");

        Assert.Equal(MouseAction.Pressed, mouse.Action);
        Assert.Equal(MouseButton.Left, mouse.Button);
        // The terminal counts from one and everything above this counts cells.
        Assert.Equal(9, mouse.X);
        Assert.Equal(4, mouse.Y);
    }

    [Fact]
    public void A_release_is_told_apart_from_a_press()
    {
        // The lowercase final byte is the whole difference, and without it a
        // selection never learns that it ended.
        Assert.Equal(MouseAction.Pressed, Parse($"{Esc}[<0;1;1M").Action);
        Assert.Equal(MouseAction.Released, Parse($"{Esc}[<0;1;1m").Action);
    }

    [Fact]
    public void Each_button_is_reported_as_itself()
    {
        Assert.Equal(MouseButton.Left, Parse($"{Esc}[<0;1;1M").Button);
        Assert.Equal(MouseButton.Middle, Parse($"{Esc}[<1;1;1M").Button);
        Assert.Equal(MouseButton.Right, Parse($"{Esc}[<2;1;1M").Button);
    }

    [Fact]
    public void A_drag_is_movement_with_the_button_still_down()
    {
        // 32 is the motion bit; the low bits still say which button is held,
        // which is what a selection follows.
        var mouse = Parse($"{Esc}[<32;7;3M");

        Assert.Equal(MouseAction.Moved, mouse.Action);
        Assert.Equal(MouseButton.Left, mouse.Button);
        Assert.Equal(6, mouse.X);
        Assert.Equal(2, mouse.Y);
    }

    [Fact]
    public void The_wheel_is_a_direction_rather_than_a_button()
    {
        Assert.Equal(MouseAction.WheelUp, Parse($"{Esc}[<64;1;1M").Action);
        Assert.Equal(MouseAction.WheelDown, Parse($"{Esc}[<65;1;1M").Action);
        Assert.Equal(MouseButton.None, Parse($"{Esc}[<64;1;1M").Button);
    }

    [Fact]
    public void Held_keys_come_through_with_the_click()
    {
        Assert.Equal(KeyModifiers.Shift, Parse($"{Esc}[<4;1;1M").Modifiers);
        Assert.Equal(KeyModifiers.Alt, Parse($"{Esc}[<8;1;1M").Modifiers);
        Assert.Equal(KeyModifiers.Ctrl, Parse($"{Esc}[<16;1;1M").Modifiers);
    }

    [Fact]
    public void Several_held_keys_combine()
    {
        var mouse = Parse($"{Esc}[<20;1;1M");   // 4 shift + 16 control

        Assert.Equal(KeyModifiers.Shift | KeyModifiers.Ctrl, mouse.Modifiers);
        Assert.Equal(MouseButton.Left, mouse.Button);
    }

    [Fact]
    public void A_click_past_the_old_encodings_limit_is_read_exactly()
    {
        // Column 223 is where single-byte coordinates ran out. A wide terminal
        // is the ordinary case, not the exotic one.
        var mouse = Parse($"{Esc}[<0;400;120M");

        Assert.Equal(399, mouse.X);
        Assert.Equal(119, mouse.Y);
    }

    [Fact]
    public void A_report_is_found_among_whatever_was_typed()
    {
        var input = $"hi{Esc}[<0;3;4Mthere";

        Assert.True(MouseInput.TryParse(input, out _, out var start, out var length));

        Assert.Equal(2, start);
        Assert.Equal($"{Esc}[<0;3;4M", input.Substring(start, length));
    }

    [Fact]
    public void Typing_is_not_mistaken_for_the_mouse()
    {
        Assert.False(MouseInput.TryParse("plain text", out _, out _, out _));
        Assert.False(MouseInput.TryParse($"{Esc}[A", out _, out _, out _));
        Assert.False(MouseInput.TryParse($"{Esc}[?2026;1$y", out _, out _, out _));
    }

    [Fact]
    public void A_report_still_arriving_is_not_treated_as_keys()
    {
        Assert.True(MouseInput.CouldBePartial($"{Esc}[<0;3"));
        Assert.True(MouseInput.CouldBePartial($"{Esc}[<"));
        Assert.False(MouseInput.CouldBePartial($"{Esc}[<0;3;4M"));   // already complete
        Assert.False(MouseInput.CouldBePartial("abc"));
    }

    [Fact]
    public void Reporting_is_asked_for_and_given_back_in_the_order_that_works()
    {
        // 1006 changes how the others encode, so it goes on last and comes off
        // first; leaving it set would hand the next program a terminal that
        // reports in a mode it did not ask for.
        Assert.EndsWith($"{Esc}[?1006h", Ansi.MouseOn, StringComparison.Ordinal);
        Assert.StartsWith($"{Esc}[?1006l", Ansi.MouseOff, StringComparison.Ordinal);
    }
}
