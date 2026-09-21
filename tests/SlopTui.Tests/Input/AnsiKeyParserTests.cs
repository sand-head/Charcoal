using SlopTui.Input;

namespace SlopTui.Tests.Input;

public class AnsiKeyParserTests
{
    private const long Ticks = TimeSpan.TicksPerMillisecond;

    private const Key Up = Key.Up;
    private const Key Left = Key.Left;

    private static List<InputEvent> FeedWhole(string text) => new AnsiKeyParser().Feed(text, 0).Events;

    /// <summary>The single event, asserted to be a key.</summary>
    private static KeyEvent SingleKey(IEnumerable<InputEvent> events) => Assert.IsType<KeyEvent>(Assert.Single(events));

    // Arrows, Home/End (TG encoder: CursorUp "\e[A" ... End "\e[F")

    [Theory]
    [InlineData("\u001B[A", Key.Up)]
    [InlineData("\u001B[B", Key.Down)]
    [InlineData("\u001B[C", Key.Right)]
    [InlineData("\u001B[D", Key.Left)]
    [InlineData("\u001B[H", Key.Home)]
    [InlineData("\u001B[F", Key.End)]
    public void Cursor_keys(string sequence, Key expected)
    {
        var key = SingleKey(FeedWhole(sequence));
        Assert.Equal(expected, key.Key);
        Assert.Equal(KeyModifiers.None, key.Modifiers);
    }

    // Tilde codes (TG encoder: Insert "\e[2~" ... F12 "\e[24~")

    [Theory]
    [InlineData("\u001B[2~", Key.Insert)]   // Insert
    [InlineData("\u001B[3~", Key.Delete)]   // Delete
    [InlineData("\u001B[5~", Key.PageUp)]   // PageUp
    [InlineData("\u001B[6~", Key.PageDown)]   // PageDown
    [InlineData("\u001B[15~", Key.F5)] // F5
    [InlineData("\u001B[17~", Key.F6)] // F6
    [InlineData("\u001B[18~", Key.F7)] // F7
    [InlineData("\u001B[19~", Key.F8)] // F8
    [InlineData("\u001B[20~", Key.F9)] // F9
    [InlineData("\u001B[21~", Key.F10)] // F10
    [InlineData("\u001B[23~", Key.F11)] // F11
    [InlineData("\u001B[24~", Key.F12)] // F12
    public void Tilde_codes(string sequence, Key expected)
    {
        var key = SingleKey(FeedWhole(sequence));
        Assert.Equal(expected, key.Key);
        Assert.Equal(KeyModifiers.None, key.Modifiers);
    }

    /// <summary>
    /// Home and End each have a second, older encoding; which one a terminal
    /// sends is not up to us, so both are keys.
    /// </summary>
    [Theory]
    [InlineData("\u001B[1~", Key.Home)]   // Home
    [InlineData("\u001B[4~", Key.End)]   // End
    [InlineData("\u001B[7~", Key.Home)]   // Home (rxvt)
    [InlineData("\u001B[8~", Key.End)]   // End (rxvt)
    public void Home_and_End_double_encodings(string sequence, Key expected)
    {
        var key = SingleKey(FeedWhole(sequence));
        Assert.Equal(expected, key.Key);
    }

    // SS3 (TG encoder: F1-F4 as "\eOP".."\eOS"; arrows in application mode)

    [Theory]
    [InlineData("\u001BOP", Key.F1)]
    [InlineData("\u001BOQ", Key.F2)]
    [InlineData("\u001BOR", Key.F3)]
    [InlineData("\u001BOS", Key.F4)]
    [InlineData("\u001BOA", Key.Up)]
    [InlineData("\u001BOB", Key.Down)]
    [InlineData("\u001BOC", Key.Right)]
    [InlineData("\u001BOD", Key.Left)]
    [InlineData("\u001BOH", Key.Home)]   // SS3 Home
    [InlineData("\u001BOF", Key.End)]    // SS3 End
    public void SS3_keys(string sequence, Key expected)
    {
        var key = SingleKey(FeedWhole(sequence));
        Assert.Equal(expected, key.Key);
        Assert.Equal(KeyModifiers.None, key.Modifiers);
    }

    // CSI modifiers (TG encoder: modifier 2=Shift 3=Alt 5=Ctrl 6=C+S 7=C+A 8=all)

    [Theory]
    [InlineData("\u001B[1;5A", Key.Up, 4)]   // Ctrl
    [InlineData("\u001B[1;2A", Key.Up, 1)]   // Shift
    [InlineData("\u001B[1;3A", Key.Up, 2)]   // Alt
    [InlineData("\u001B[1;6A", Key.Up, 5)]   // Shift+Ctrl
    [InlineData("\u001B[1;7A", Key.Up, 6)]   // Alt+Ctrl
    [InlineData("\u001B[1;8A", Key.Up, 7)]   // Shift+Alt+Ctrl
    [InlineData("\u001B[1;5H", Key.Home, 4)]   // Ctrl+Home
    [InlineData("\u001B[1;5F", Key.End, 4)]   // Ctrl+End
    public void CSI_letter_modifiers(string sequence, Key code, int mods)
    {
        var key = SingleKey(FeedWhole(sequence));
        Assert.Equal(code, key.Key);
        Assert.Equal((KeyModifiers)mods, key.Modifiers);
    }

    [Theory]
    [InlineData("\u001B[3;5~", Key.Delete, 4)]     // Ctrl+Delete
    [InlineData("\u001B[5;2~", Key.PageUp, 1)]     // Shift+PageUp
    [InlineData("\u001B[15;5~", Key.F5, 4)]   // Ctrl+F5
    [InlineData("\u001B[17;7~", Key.F6, 6)]   // Ctrl+Alt+F6
    [InlineData("\u001B[24;8~", Key.F12, 7)]   // Ctrl+Shift+Alt+F12
    public void Tilde_modifiers(string sequence, Key code, int mods)
    {
        var key = SingleKey(FeedWhole(sequence));
        Assert.Equal(code, key.Key);
        Assert.Equal((KeyModifiers)mods, key.Modifiers);
    }

    // Ctrl and Alt over letters

    [Theory]
    [InlineData("\u0001", 'A')]
    [InlineData("\u0003", 'C')]
    [InlineData("\u001A", 'Z')]
    public void Ctrl_letters_arrive_as_control_codes(string input, char letter)
    {
        var key = SingleKey(FeedWhole(input));
        Assert.Equal((Key)letter, key.Key);
        Assert.Equal(KeyModifiers.Ctrl, key.Modifiers);
    }

    [Theory]
    [InlineData("\u001Bx", 'X', 0)]          // Alt+x, no shift
    [InlineData("\u001BA", 'A', 1)]          // Alt+Shift+A: uppercase means Shift
    [InlineData("\u001B1", '1', 0)]          // punctuation keeps its codepoint
    public void Alt_letters_as_esc_prefix(string input, char expected, int shift)
    {
        var key = SingleKey(FeedWhole(input));
        Assert.Equal(KeyModifiers.Alt | (KeyModifiers)shift, key.Modifiers);
        Assert.Equal((Key)expected, key.Key);
    }

    [Fact]
    public void ESC_ESC_sequence_is_alt_over_the_sequence()
    {
        var key = SingleKey(FeedWhole("\u001B\u001B[A"));
        Assert.Equal(Up, key.Key);
        Assert.Equal(KeyModifiers.Alt, key.Modifiers);
    }

    [Fact]
    public void ESC_ESC_split_across_reads_stays_alt()
    {
        var parser = new AnsiKeyParser();
        var first = parser.Feed("\u001B\u001B", 0);
        Assert.Empty(first.Events);

        var second = parser.Feed("[A", 6 * Ticks);
        AltOver(Assert.Single(second.Events), Up);
    }

    [Fact]
    public void ESC_ESC_plus_modifiers_is_alt_over_those_modifiers()
    {
        var key = SingleKey(FeedWhole("\u001B\u001B[1;5A"));
        Assert.Equal(Up, key.Key);
        Assert.Equal(KeyModifiers.Alt | KeyModifiers.Ctrl, key.Modifiers);
    }

    // Plain text

    [Theory]
    [InlineData("a", 0)]   // None
    [InlineData("Z", 1)]   // Shift: an uppercase letter means Shift was held
    [InlineData("5", 0)]
    [InlineData("!", 0)]
    public void One_printable_char_is_one_key(string input, int mods)
    {
        var key = SingleKey(FeedWhole(input));
        Assert.Equal((KeyModifiers)mods, key.Modifiers);
        var expected = input[0] is >= 'a' and <= 'z'
            ? (Key)(input[0] - 'a' + 'A')
            : (Key)input[0];
        Assert.Equal(expected, key.Key);
        Assert.Equal(input, key.Text);
    }

    [Fact]
    public void A_grapheme_cluster_arrives_as_one_keystroke()
    {
        // e + combining acute, then a flag: two clusters, two events, and the
        // combining mark is not an event of its own.
        var events = FeedWhole("e\u0301\U0001F1FA\U0001F1F8").Cast<KeyEvent>().ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal("e\u0301", events[0].Text);
        Assert.Equal((Key)'E', events[0].Key);   // the letter key, un-shifted
        Assert.Equal((Key)0x1F1FA, events[1].Key);
        Assert.Equal("\U0001F1FA\U0001F1F8", events[1].Text);
    }

    [Fact]
    public void An_unpaired_high_surrogate_is_held_not_typed()
    {
        var parser = new AnsiKeyParser();
        var first = parser.Feed("\uD83D", 0);
        Assert.Empty(first.Events);
        Assert.Equal(0, first.Consumed);

        var second = parser.Feed("\uDE00", 5 * Ticks);
        var key = SingleKey(second.Events);
        Assert.Equal((Key)0x1F600, key.Key);   // the surrogate pair folded
    }

    [Fact]
    public void Text_before_and_after_a_sequence()
    {
        var events = FeedWhole("a\u001B[Dz").Cast<KeyEvent>().ToList();
        Assert.Equal(3, events.Count);
        Assert.Equal((Key)'A', events[0].Key);
        Assert.Equal(Left, events[1].Key);
        Assert.Equal((Key)'Z', events[2].Key);
    }

    // Enter, Tab, Backspace

    [Theory]
    [InlineData("\r", Key.Enter)]
    [InlineData("\n", Key.Enter)]
    [InlineData("\t", Key.Tab)]
    [InlineData("\u0008", Key.Backspace)]
    [InlineData("\u007F", Key.Backspace)]
    public void Enter_tab_backspace(string input, Key code)
    {
        var key = SingleKey(FeedWhole(input));
        Assert.Equal(code, key.Key);
        Assert.Equal(KeyModifiers.None, key.Modifiers);
    }

    // Bracketed paste

    [Fact]
    public void A_paste_is_one_event_however_long()
    {
        var body = "line one\nline two next\r\nwith\ttabs and \u001B[31mcolour\u001B[0m";
        var paste = Assert.IsType<PasteEvent>(Assert.Single(FeedWhole($"\u001B[200~{body}\u001B[201~")));
        Assert.Equal(body, paste.Text);
    }

    [Fact]
    public void A_paste_body_that_contains_paste_markers_does_not_nest()
    {
        var body = "outer \u001B[200~inner\u001B[201~ tail";
        var paste = Assert.IsType<PasteEvent>(Assert.Single(FeedWhole($"\u001B[200~{body}\u001B[201~")));
        Assert.Equal("outer inner tail", paste.Text);
    }

    [Fact]
    public void A_paste_split_across_reads_comes_out_whole()
    {
        var parser = new AnsiKeyParser();
        var first = parser.Feed("\u001B[200~split ", 0);
        Assert.Empty(first.Events);

        var second = parser.Feed("across\u001B[201~", 3 * Ticks);
        var paste = Assert.IsType<PasteEvent>(Assert.Single(second.Events));
        Assert.Equal("split across", paste.Text);
    }

    [Fact]
    public void Keys_around_a_paste_are_keys_and_the_paste_is_one_event()
    {
        var events = FeedWhole("x\u001B[200~body\u001B[201~y");
        Assert.Equal(3, events.Count);
        Assert.Equal((Key)'X', Assert.IsType<KeyEvent>(events[0]).Key);
        Assert.Equal("body", Assert.IsType<PasteEvent>(events[1]).Text);
        Assert.Equal((Key)'Y', Assert.IsType<KeyEvent>(events[2]).Key);
    }

    // Mouse

    [Fact]
    public void An_SGR_press_is_a_mouse_event_with_the_button_and_cell()
    {
        var mouse = Assert.IsType<MouseEvent>(Assert.Single(FeedWhole("\u001B[<0;33;12M")));
        Assert.Equal(MouseAction.Pressed, mouse.Action);
        Assert.Equal(MouseButton.Left, mouse.Button);
        Assert.Equal(32, mouse.X);
        Assert.Equal(11, mouse.Y);
    }

    [Fact]
    public void A_release_is_the_lowercase_final()
    {
        var mouse = Assert.IsType<MouseEvent>(Assert.Single(FeedWhole("\u001B[<0;1;1m")));
        Assert.Equal(MouseAction.Released, mouse.Action);
    }

    [Fact]
    public void A_drag_combines_the_motion_bit_with_the_button()
    {
        var mouse = Assert.IsType<MouseEvent>(Assert.Single(FeedWhole("\u001B[<32;5;7M")));
        Assert.Equal(MouseAction.Moved, mouse.Action);
        Assert.Equal(MouseButton.Left, mouse.Button);
    }

    [Fact]
    public void The_wheel_decodes_to_a_direction()
    {
        var up = Assert.IsType<MouseEvent>(FeedWhole("\u001B[<64;1;1M").Single());
        var down = Assert.IsType<MouseEvent>(FeedWhole("\u001B[<65;1;1M").Single());
        Assert.Equal(MouseAction.WheelUp, up.Action);
        Assert.Equal(MouseAction.WheelDown, down.Action);
    }

    [Fact]
    public void A_half_mouse_report_is_held()
    {
        var parser = new AnsiKeyParser();
        var first = parser.Feed("\u001B[<0;1", 0);
        Assert.Empty(first.Events);

        var second = parser.Feed(";2M", 4 * Ticks);
        Assert.Single(second.Events);
    }

    // Focus and terminal replies

    [Fact]
    public void Focus_events_are_consumed()
    {
        var focus = Assert.IsType<FocusEvent>(Assert.Single(FeedWhole("\u001B[I")));
        Assert.True(focus.Gained);

        focus = Assert.IsType<FocusEvent>(Assert.Single(FeedWhole("\u001B[O")));
        Assert.False(focus.Gained);
    }

    [Fact]
    public void A_DA1_reply_is_consumed()
    {
        var reply = Assert.IsType<ReplyEvent>(Assert.Single(FeedWhole("\u001B[?62;6c")));
    }

    [Fact]
    public void A_DECRPM_reply_is_consumed()
    {
        var reply = Assert.IsType<ReplyEvent>(Assert.Single(FeedWhole("\u001B[?2026;2$y")));
    }

    [Fact]
    public void A_cursor_position_reply_is_consumed()
    {
        var reply = Assert.IsType<ReplyEvent>(Assert.Single(FeedWhole("\u001B[24;80R")));
    }

    [Fact]
    public void A_kitty_flags_answer_is_consumed()
    {
        var reply = Assert.IsType<ReplyEvent>(Assert.Single(FeedWhole("\u001B[?1u")));
    }

    [Fact]
    public void An_XTGETTCAP_answer_is_consumed()
    {
        var reply = Assert.IsType<ReplyEvent>(Assert.Single(FeedWhole("\u001BP1+r5448\u001B\\")));
    }

    // Kitty CSI u

    [Fact]
    public void Kitty_plain_letter_normalises_to_the_key_value()
    {
        var key = SingleKey(FeedWhole("\u001B[97u"));
        Assert.Equal((Key)'A', key.Key);
        Assert.Equal(KeyModifiers.None, key.Modifiers);
    }

    [Fact]
    public void Kitty_flag4_alternate_key_field_is_tolerated()
    {
        var key = SingleKey(FeedWhole("\u001B[97:65u"));
        Assert.Equal((Key)'A', key.Key);
    }

    [Theory]
    [InlineData("\u001B[97;5u", (Key)'A', 4)]            // Ctrl+A
    [InlineData("\u001B[97;2u", (Key)'A', 1)]            // Shift+a
    [InlineData("\u001B[105;3u", (Key)'I', 2)]           // Alt+i
    [InlineData("\u001B[57344;5u", Key.Up, 4)]     // Ctrl+Up
    public void Kitty_modifiers(string sequence, Key code, int mods)
    {
        var key = SingleKey(FeedWhole(sequence));
        Assert.Equal(code, key.Key);
        Assert.Equal((KeyModifiers)mods, key.Modifiers);
    }

    [Theory]
    [InlineData("\u001B[57344u", Key.Up)]   // Up
    [InlineData("\u001B[57345u", Key.Down)]   // Down
    [InlineData("\u001B[57346u", Key.Left)]   // Left
    [InlineData("\u001B[57347u", Key.Right)]   // Right
    [InlineData("\u001B[57348u", Key.PageUp)]   // PageUp
    [InlineData("\u001B[57349u", Key.PageDown)]   // PageDown
    [InlineData("\u001B[57350u", Key.Home)]   // Home
    [InlineData("\u001B[57351u", Key.End)]   // End
    [InlineData("\u001B[57352u", Key.Insert)]   // Insert
    [InlineData("\u001B[57353u", Key.Delete)]   // Delete
    [InlineData("\u001B[13u", Key.Enter)]                // Enter
    [InlineData("\u001B[57364u", Key.F1)]  // F1
    [InlineData("\u001B[57369u", Key.F6)]  // F6
    [InlineData("\u001B[57375u", Key.F12)]  // F12
    public void Kitty_functional_keys(string sequence, Key expected)
    {
        var key = SingleKey(FeedWhole(sequence));
        Assert.Equal(expected, key.Key);
    }

    [Fact]
    public void Kitty_release_events_are_consumed_silently()
    {
        var release = Assert.IsType<ReplyEvent>(Assert.Single(FeedWhole("\u001B[97;1:3u")));
    }

    [Fact]
    public void Kitty_repeat_is_a_key()
    {
        var key = SingleKey(FeedWhole("\u001B[97;1:2u"));
        Assert.Equal((Key)'A', key.Key);
    }

    // The ESC gap

    [Fact]
    public void A_lone_ESC_held_within_the_gap_is_not_the_Esc_key()
    {
        var parser = new AnsiKeyParser();
        Assert.Empty(parser.Feed("\u001B", 0).Events);
        Assert.Empty(parser.Feed("", 7 * Ticks).Events);
    }

    [Fact]
    public void A_lone_ESC_after_the_gap_is_the_Esc_key()
    {
        var parser = new AnsiKeyParser();
        parser.Feed("\u001B", 0);
        var later = parser.Feed("", 9 * Ticks);
        var key = SingleKey(later.Events);
        Assert.Equal(Key.Escape, key.Key);
    }

    [Fact]
    public void The_gap_measures_from_the_ESCs_own_arrival()
    {
        var parser = new AnsiKeyParser();
        parser.Feed("\u001B", 0);
        parser.Feed("", 9 * Ticks);   // the Esc key fired here

        // A second ESC has its own gap from its own arrival.
        var third = parser.Feed("\u001B", 9 * Ticks);
        Assert.Empty(third.Events);
    }

    [Fact]
    public void ESC_then_a_char_in_a_later_read_within_the_gap_is_Alt()
    {
        var parser = new AnsiKeyParser();
        parser.Feed("\u001B", 0);
        var later = parser.Feed("x", 6 * Ticks);
        AltOver(Assert.Single(later.Events), (Key)'X');
    }

    [Fact]
    public void ESC_then_a_char_in_the_same_read_is_Alt_regardless_of_the_gap()
    {
        var parser = new AnsiKeyParser();
        // The user pressed Esc, paused, and typed — but the reader delivered
        // them together. A pause inside one read cannot be seen, so the bytes
        // are Alt; only a read boundary and the gap separate them.
        var result = parser.Feed("\u001Bk", 50 * Ticks);
        Assert.Equal(KeyModifiers.Alt, SingleKey(result.Events).Modifiers);
    }

    [Fact]
    public void ESC_then_a_mouse_report_is_a_mouse_event_not_the_Esc_key()
    {
        var single = FeedWhole("\u001B\u001B[<0;1;1M");
        Assert.IsType<MouseEvent>(Assert.Single(single));
    }

    // Unrecognised: passed through

    [Theory]
    [InlineData("\u001B[10~")]     // an unassigned tilde code
    [InlineData("\u001B[5A")]      // cursor-movement shape, not a key we asked for
    [InlineData("\u001B[1;2;3A")]  // three parameters, not a shape we asked for
    public void Unknown_sequences_are_passed_through_whole(string sequence)
    {
        var parser = new AnsiKeyParser();
        var result = parser.Feed(sequence, 0);
        Assert.Empty(result.Events);
        Assert.Equal(sequence, result.Unrecognised.ToString());
    }

    [Fact]
    public void A_long_unknown_sequence_is_eventually_passed_through()
    {
        var parser = new AnsiKeyParser();
        var result = parser.Feed("\u001B[1234567890123456789012345678901234~", 0);
        Assert.Empty(result.Events);
        Assert.True(result.Unrecognised.Length > 0);
    }

    [Fact]
    public void Unrecognised_bytes_do_not_lag_a_following_key()
    {
        var parser = new AnsiKeyParser();
        var result = parser.Feed("\u001B[10~x", 0);
        var key = SingleKey(result.Events);
        Assert.Equal((Key)'X', key.Key);
        Assert.Equal("\u001B[10~", result.Unrecognised.ToString());
    }

    [Fact]
    public void An_unknown_SS3_letter_is_passed_through()
    {
        var parser = new AnsiKeyParser();
        var result = parser.Feed("\u001BOT", 0);
        Assert.Empty(result.Events);
        Assert.Equal("\u001BOT", result.Unrecognised.ToString());
    }

    // OSC replies: three terminators, held until one arrives

    [Fact]
    public void An_OSC_reply_terminated_by_ST_is_consumed()
    {
        var reply = Assert.IsType<ReplyEvent>(Assert.Single(FeedWhole("\u001B]11;rgb:ffff/ffff/ffff\u001B\\")));
    }

    [Fact]
    public void An_OSC_reply_terminated_by_BEL_is_consumed()
    {
        var reply = Assert.IsType<ReplyEvent>(Assert.Single(FeedWhole("\u001B]11;rgb:0000/0000/0000\a")));
    }

    [Fact]
    public void An_OSC_reply_terminated_by_0x9C_is_consumed()
    {
        var reply = Assert.IsType<ReplyEvent>(Assert.Single(FeedWhole("\u001B]11;rgb:1010/2020/3030\u009C")));
    }

    [Fact]
    public void An_OSC_reply_split_across_reads_is_held_then_consumed()
    {
        var parser = new AnsiKeyParser();
        var first = parser.Feed("\u001B]11;rgb:ffff/f", 0);
        Assert.Empty(first.Events);

        var second = parser.Feed("fff/ffff\u001B\\", 3 * Ticks);
        Assert.IsType<ReplyEvent>(Assert.Single(second.Events));
    }

    [Fact]
    public void An_OSC_reply_is_not_Alt_bracket()
    {
        // Before the fix this parsed as Alt+] followed by text keys.
        var reply = Assert.IsType<ReplyEvent>(Assert.Single(FeedWhole("\u001B]11;rgb:0/0/0\a")));
        Assert.StartsWith("\u001B]11", reply.Sequence, StringComparison.Ordinal);
    }

    // Kitty CSI u, positional fields

    [Fact]
    public void Kitty_four_fields_with_associated_text()
    {
        // key;mods:event;shifted — the shifted key of 'x' is 'X' (120), so the
        // typed text is "x" from the key codepoint.
        var key = SingleKey(FeedWhole("\u001B[120;1:1;120u"));
        Assert.Equal((Key)'X', key.Key);
        Assert.Equal("x", key.Text);
        Assert.Equal(KeyModifiers.None, key.Modifiers);
    }

    [Fact]
    public void Kitty_typing_stream_splits_without_loss()
    {
        // Three CSI-u keystrokes split across reads in the nastiest way the
        // input thread can produce: mid-parameters. Every character typed
        // must arrive exactly once, whatever the read boundaries.
        var parser = new AnsiKeyParser();
        var typed = "\u001B[97;1:1;97u\u001B[98;1:1;98u\u001B[99;1:1;99u";
        var keys = new List<InputEvent>();
        foreach (var (chunk, _) in new[] { (typed[..5], 0L), (typed[5..12], 1L), (typed[12..], 2L) })
            keys.AddRange(parser.Feed(chunk, 1_000_000 * (1 + Array.IndexOf(new[] { typed[..5], typed[5..12], typed[12..] }, chunk))).Events);
        Assert.Equal(new[] { "a", "b", "c" }, keys.Cast<KeyEvent>().Select(k => k.Text).ToArray());
    }

    [Fact]
    public void Kitty_letter_with_associated_text_field_consumes_exactly_the_sequence()
    {
        // The shape a kitty terminal under flag 31 sends for plain typing:
        // key;mods:event;text. The whole sequence must be one keystroke, with
        // nothing left over — a trailing fragment here is a leaked keystroke.
        var parser = new AnsiKeyParser();
        var result = parser.Feed("\u001B[104;1:1;104u", 1_000);
        var key = SingleKey(result.Events);
        Assert.Equal((Key)'H', key.Key);
        Assert.Equal("h", key.Text);
        Assert.True(result.Unrecognised.IsEmpty);
        Assert.Equal("\u001B[104;1:1;104u".Length, result.Consumed);
    }

    [Fact]
    public void Kitty_text_field_may_carry_colon_separated_codepoints()
    {
        // Terminal.Gui's grammar: key:shifted:base ; mods:event ; text — the
        // text field itself is a colon-separated codepoint list.
        var key = SingleKey(FeedWhole("\u001B[120;1:1;120:120u"));
        Assert.Equal((Key)'X', key.Key);
        Assert.Equal("xx", key.Text);
    }

    [Fact]
    public void Kitty_shifted_alternate_in_field_one_gives_the_shifted_text()
    {
        // key:shifted — 97:65 is 'a' with shifted alternate 'A', mods Shift.
        var key = SingleKey(FeedWhole("\u001B[97:65;2u"));
        Assert.Equal((Key)'A', key.Key);
        Assert.Equal(KeyModifiers.Shift, key.Modifiers);
        Assert.Equal("A", key.Text);
    }

    [Fact]
    public void Kitty_shifted_tab_alternate_release_is_consumed()
    {
        // 57441 is the shifted-Tab alternate; 1:3 is a release, and releases
        // are consumed silently however many fields follow the event type.
        var release = Assert.IsType<ReplyEvent>(Assert.Single(FeedWhole("\u001B[57441;1:3;57441u")));
    }

    [Fact]
    public void Kitty_bare_modifier_key_is_consumed()
    {
        // 57441 is the Left Shift modifier key itself, not Shift+something:
        // Terminal.Gui maps it to a modifier-only key and slop ignores it.
        var bare = Assert.IsType<ReplyEvent>(Assert.Single(FeedWhole("\u001B[57441u")));
    }

    [Fact]
    public void Kitty_shift_plus_tab_is_Tab_with_shift()
    {
        var key = SingleKey(FeedWhole("\u001B[9;2u"));
        Assert.Equal(Key.Tab, key.Key);
        Assert.Equal(KeyModifiers.Shift, key.Modifiers);
    }

    [Fact]
    public void Kitty_shift_plus_letter()
    {
        var key = SingleKey(FeedWhole("\u001B[97;2u"));
        Assert.Equal((Key)'A', key.Key);
        Assert.Equal(KeyModifiers.Shift, key.Modifiers);
        Assert.Equal("A", key.Text);
    }

    // The ESC gap, pinned

    [Fact]
    public void ESC_then_key_in_the_same_read_is_Alt_regardless_of_the_gap()
    {
        var parser = new AnsiKeyParser();
        var result = parser.Feed("\u001Bk", 50 * Ticks);
        AltOver(Assert.Single(result.Events), (Key)'K');
    }

    [Fact]
    public void ESC_then_key_in_different_reads_within_the_gap_is_Alt()
    {
        var parser = new AnsiKeyParser();
        parser.Feed("\u001B", 0);
        var later = parser.Feed("k", 6 * Ticks);
        AltOver(Assert.Single(later.Events), (Key)'K');
    }

    [Fact]
    public void A_lone_ESC_stays_pending_with_no_event_until_a_later_Feed()
    {
        var parser = new AnsiKeyParser();
        parser.Feed("\u001B", 0);
        Assert.True(parser.EscPending);

        // Within the gap, even with a call: still pending, nothing fired.
        Assert.Empty(parser.Feed("", 5 * Ticks).Events);
        Assert.True(parser.EscPending);

        // Past the gap, the next call is what fires it — the parser itself
        // never wakes up on its own.
        var later = parser.Feed("", 9 * Ticks);
        var esc = SingleKey(later.Events);
        Assert.Equal(Key.Escape, esc.Key);
        Assert.False(parser.EscPending);
    }

    // Helpers

    private static void AltOver(InputEvent e, Key code)
    {
        var key = Assert.IsType<KeyEvent>(e);
        Assert.Equal(code, key.Key);
        Assert.Equal(KeyModifiers.Alt, key.Modifiers);
    }

    [Fact]
    public void Back_tab_is_shift_tab()
    {
        var parser = new AnsiKeyParser();
        var result = parser.Feed("\e[Z", 0);
        var key = Assert.IsType<KeyEvent>(Assert.Single(result.Events));
        Assert.Equal(Key.Tab, key.Key);
        Assert.Equal(KeyModifiers.Shift, key.Modifiers);
    }
}
