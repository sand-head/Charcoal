using System.Globalization;
using System.Text;

namespace Charcoal.Input;

/// <summary>What one <see cref="AnsiKeyParser.Feed"/> made of the text it was given.</summary>
/// <param name="Events">Everything recognised, in arrival order.</param>
/// <param name="Consumed">How many characters of the fed text are finished with; the rest is held.</param>
/// <param name="Unrecognised">Sequences the parser does not know, verbatim.</param>
public readonly record struct ParseResult(
    List<InputEvent> Events,
    int Consumed,
    ReadOnlyMemory<char> Unrecognised);

/// <summary>
/// Parses terminal input into keys, pastes, mouse reports and replies.
/// </summary>
/// <remarks>
/// The parser does no I/O and keeps no clock. Each call carries the time its
/// text was read, and a lone ESC becomes the Esc key only once
/// <see cref="EscGapTicks"/> have passed without a following byte. Between
/// calls it holds only what a split read left unfinished.
/// Ported from slopcoder's <c>Render/KeyInput.cs</c>.
/// </remarks>
public sealed class AnsiKeyParser
{
    /// <summary>How long a lone ESC waits for the rest of a sequence before it is the Esc key.</summary>
    public static readonly long EscGapTicks = TimeSpan.FromMilliseconds(8).Ticks;

    private const int MaxSequence = 32;
    private const string PasteStart = "\e[200~";
    private const string PasteEnd = "\e[201~";

    private string _pending = "";
    private long _escAt;
    private bool _escPending;
    private bool _altCarry;

    /// <summary>Whether a lone ESC is waiting out its gap.</summary>
    public bool EscPending => _escPending;

    /// <summary>When the pending lone ESC becomes the Esc key, or null.</summary>
    public long? EscDeadlineTicks => _escPending ? _escAt + EscGapTicks : null;

    /// <summary>
    /// Parses text read at <paramref name="nowTicks"/>. Unfinished input is
    /// held and completed by the next call.
    /// </summary>
    public ParseResult Feed(ReadOnlySpan<char> text, long nowTicks)
    {
        var events = new List<InputEvent>();

        if (_escPending && nowTicks - _escAt >= EscGapTicks)
        {
            events.Add(new KeyEvent(Key.Escape, KeyModifiers.None, ""));
            _pending = "";
            _escPending = false;
            _altCarry = false;
        }

        var priorPending = _pending.Length;
        var carriedEsc = _escPending;
        var work = priorPending == 0 ? text.ToString() : string.Concat(_pending.AsSpan(), text);
        _pending = "";
        _escPending = false;

        var passthrough = new StringBuilder();
        var i = 0;
        while (i < work.Length)
        {
            var consumed = ParseOne(work.AsSpan(i), out var inputEvent, out var altPrefix);
            if (altPrefix)
            {
                _altCarry = true;
                var keyNotYetRead = i + 1 >= work.Length;
                if (keyNotYetRead) break;
                i++;
                continue;
            }
            if (consumed == 0) break;
            if (consumed < 0)
            {
                passthrough.Append(work, i, -consumed);
                i -= consumed;
                _altCarry = false;
                continue;
            }

            if (_altCarry)
            {
                if (inputEvent is KeyEvent key)
                {
                    inputEvent = key with { Modifiers = key.Modifiers | KeyModifiers.Alt };
                }
                _altCarry = false;
            }

            if (inputEvent is { } ready) events.Add(ready);
            i += consumed;
        }

        var held = work.Length - i;
        if (held > 0)
        {
            _pending = work[i..];
            if (_pending == "\e")
            {
                _escPending = true;
                // An ESC carried over keeps its first stamp, or a polling
                // caller could hold the gap open forever.
                if (!carriedEsc) _escAt = nowTicks;
            }
        }

        var heldFromText = Math.Min(text.Length, Math.Max(0, held - priorPending));
        var unrecognised = passthrough.Length == 0 ? default : passthrough.ToString().AsMemory();
        return new ParseResult(events, text.Length - heldFromText, unrecognised);
    }

    /// <summary>Parses the first item of the text.</summary>
    /// <returns>
    /// The length consumed; zero when the item is unfinished; or the negated
    /// length of an unrecognised sequence to pass through.
    /// </returns>
    private int ParseOne(ReadOnlySpan<char> text, out InputEvent? inputEvent, out bool altPrefix)
    {
        altPrefix = false;
        if (text[0] != '\e') return ParsePlain(text, out inputEvent);

        inputEvent = null;
        if (text.Length == 1) return 0;

        // Terminals send Alt+arrow and the like as ESC followed by the sequence.
        if (text[1] == '\e')
        {
            altPrefix = true;
            return 1;
        }

        if (text[1] == '[' && text.Length >= 3 && text[2] is 'I' or 'O')
        {
            inputEvent = new FocusEvent(text[2] == 'I');
            return 3;
        }

        if (text.StartsWith("\e[?") && text.Length > 3)
        {
            var reply = ParsePrivateReply(text, out inputEvent);
            if (reply != null) return reply.Value;
        }

        if (text[1] == ']') return ParseTerminatedReply(text, OscEnd(text), out inputEvent);
        if (text[1] is 'P' or '_') return ParseTerminatedReply(text, StringTerminatorEnd(text), out inputEvent);
        if (text.StartsWith("\e[<")) return ParseMouse(text, out inputEvent);
        if (text.StartsWith(PasteStart)) return ParsePaste(text, out inputEvent);

        if (text[1] == '[')
        {
            if (text.Length < 3) return 0;
            return ParseCsi(text, out inputEvent);
        }

        if (text[1] == 'O') return ParseSs3(text, out inputEvent);

        return ParseAltKey(text, out inputEvent);
    }

    private static int ParsePlain(ReadOnlySpan<char> text, out InputEvent? inputEvent)
    {
        inputEvent = null;
        var first = text[0];
        var key = first switch
        {
            '\r' or '\n' => Key.Enter,
            '\t' => Key.Tab,
            (char)0x08 or (char)0x7F => Key.Backspace,
            _ => Key.None,
        };
        if (key != Key.None)
        {
            inputEvent = new KeyEvent(key, KeyModifiers.None, "");
            return 1;
        }
        if (IsCtrlLetter(first))
        {
            inputEvent = new KeyEvent(CtrlLetter(first), KeyModifiers.Ctrl, "");
            return 1;
        }

        var lowSurrogateMissing = char.IsHighSurrogate(first) && (text.Length == 1 || !char.IsLowSurrogate(text[1]));
        if (lowSurrogateMissing) return 0;

        var length = StringInfo.GetNextTextElementLength(text);
        if (length <= 0) return 0;
        var cluster = text[..length];
        inputEvent = new KeyEvent(KeyOf(cluster), ModifiersOf(cluster), cluster.ToString());
        return length;
    }

    /// <summary>
    /// Replies that start <c>CSI ?</c>: DA1, DECRPM and the kitty keyboard
    /// flags. Returns null for any other <c>CSI ?</c> sequence.
    /// </summary>
    private static int? ParsePrivateReply(ReadOnlySpan<char> text, out InputEvent? inputEvent)
    {
        inputEvent = null;
        var end = 3;
        while (end < text.Length && IsReplyByte(text[end])) end++;
        if (end >= text.Length) return HoldOrJunk(text);
        if (text[end] is not ('c' or 'y' or 'u')) return null;

        inputEvent = Reply(text[..(end + 1)]);
        return end + 1;
    }

    private static int ParseTerminatedReply(ReadOnlySpan<char> text, int end, out InputEvent? inputEvent)
    {
        inputEvent = null;
        if (end < 0) return HoldOrJunk(text);
        inputEvent = Reply(text[..end]);
        return end;
    }

    private static int ParseMouse(ReadOnlySpan<char> text, out InputEvent? inputEvent)
    {
        inputEvent = null;
        if (MouseInput.TryParse(text, out var mouse, out var start, out var length) && start == 0)
        {
            inputEvent = mouse;
            return length;
        }
        return MouseInput.CouldBePartial(text) ? 0 : -text.Length;
    }

    private static int ParsePaste(ReadOnlySpan<char> text, out InputEvent? inputEvent)
    {
        inputEvent = null;
        var end = PasteBodyEnd(text);
        if (end < 0) return 0;

        var body = text[PasteStart.Length..end].ToString();
        inputEvent = new PasteEvent(WithoutPasteMarkers(body));
        return end + PasteEnd.Length;
    }

    private static int ParseSs3(ReadOnlySpan<char> text, out InputEvent? inputEvent)
    {
        inputEvent = null;
        if (text.Length < 3) return 0;
        if (!Ss3Keys.TryGetValue(text[2], out var key)) return -3;

        inputEvent = new KeyEvent(key, KeyModifiers.None, "");
        return 3;
    }

    /// <summary>ESC followed by a key is that key with Alt.</summary>
    private static int ParseAltKey(ReadOnlySpan<char> text, out InputEvent? inputEvent)
    {
        inputEvent = null;
        var length = StringInfo.GetNextTextElementLength(text[1..]);
        if (length <= 0) return 0;

        var cluster = text.Slice(1, length);
        if (IsCtrlLetter(cluster[0]))
        {
            inputEvent = new KeyEvent(CtrlLetter(cluster[0]), KeyModifiers.Ctrl | KeyModifiers.Alt, "");
        }
        else
        {
            inputEvent = new KeyEvent(KeyOf(cluster), KeyModifiers.Alt | ModifiersOf(cluster), cluster.ToString());
        }
        return 1 + length;
    }

    /// <summary>
    /// Cursor and editing keys, kitty <c>CSI u</c> keys, and status replies.
    /// </summary>
    private static int ParseCsi(ReadOnlySpan<char> text, out InputEvent? inputEvent)
    {
        inputEvent = null;

        var hasPrivateMarker = text[2] is '?' or '>' or '<' or '=';
        var parametersStart = hasPrivateMarker ? 3 : 2;
        var final = parametersStart;
        while (final < text.Length && (char.IsAsciiDigit(text[final]) || text[final] is ';' or ':')) final++;
        if (final >= text.Length) return HoldOrJunk(text);

        var length = final + 1;
        var command = text[final];
        var parameters = text[parametersStart..final];
        if (hasPrivateMarker) return -length;

        if (command is 'R' or 'n' or 't' && IsNumbersAndSemicolons(parameters))
        {
            inputEvent = Reply(text[..length]);
            return length;
        }

        switch (command)
        {
            case 'u':
                return ParseKitty(text, parameters, length, out inputEvent);

            case '~':
                if (!TryParseParameters(parameters, out var code, out var modifiers)) return -length;
                if (!TildeKeys.TryGetValue(code, out var key)) return -length;
                inputEvent = new KeyEvent(key, DecodeModifiers(modifiers ?? 1), "");
                return length;

            case 'Z' when parameters.IsEmpty:
                inputEvent = new KeyEvent(Key.Tab, KeyModifiers.Shift, "");
                return length;

            case 'A' or 'B' or 'C' or 'D' or 'H' or 'F':
                if (parameters.IsEmpty)
                {
                    inputEvent = new KeyEvent(LetterKey(command), KeyModifiers.None, "");
                    return length;
                }
                if (!TryParseParameters(parameters, out var one, out var letterModifiers)) return -length;
                if (one != 1 || letterModifiers is null) return -length;
                inputEvent = new KeyEvent(LetterKey(command), DecodeModifiers(letterModifiers.Value), "");
                return length;

            default:
                return -length;
        }
    }

    /// <summary>
    /// A kitty <c>CSI key[:shifted[:base]] ; modifiers[:event] ; text u</c> key.
    /// Releases and bare modifier keys are consumed as replies.
    /// </summary>
    private static int ParseKitty(ReadOnlySpan<char> text, ReadOnlySpan<char> parameters, int length, out InputEvent? inputEvent)
    {
        inputEvent = null;

        var keyField = NextField(ref parameters, ';');
        var modifierField = NextField(ref parameters, ';');
        var textField = parameters;

        if (!NumberWhole(NextField(ref keyField, ':'), out var keyNumber)) return -length;
        var shifted = NumberWhole(NextField(ref keyField, ':'), out var alternate) && alternate > 0 ? (uint)alternate : 0;

        if (IsKittyModifierKey(keyNumber))
        {
            inputEvent = Reply(text[..length]);
            return length;
        }

        var key = KittyKey(keyNumber);
        if (key == Key.None) return -length;
        if (key is >= (Key)'a' and <= (Key)'z') key -= 'a' - 'A';

        var modifiers = KeyModifiers.None;
        if (!modifierField.IsEmpty)
        {
            var modifierNumber = NextField(ref modifierField, ':');
            var eventType = modifierField;
            var isPressOrRepeat = eventType.IsEmpty || eventType is "1" or "2";
            if (!isPressOrRepeat)
            {
                inputEvent = Reply(text[..length]);
                return length;
            }
            if (!NumberWhole(modifierNumber, out var modifierValue)) return -length;
            modifiers = DecodeModifiers(modifierValue);
        }

        var typed = IsPrintable(key) ? KittyText(keyNumber, shifted, modifiers, textField) : "";
        inputEvent = new KeyEvent(key, modifiers, typed);
        return length;
    }

    /// <summary>
    /// The associated text if the terminal sent it, else the shifted key, else
    /// the key's own codepoint, upper-cased when Shift is held.
    /// </summary>
    private static string KittyText(int code, uint shifted, KeyModifiers modifiers, ReadOnlySpan<char> textField)
    {
        if (TryAssociatedText(textField, out var associated)) return associated;
        if (shifted > 0) return TextOf(shifted);

        var text = TextOf((uint)code);
        return modifiers.HasFlag(KeyModifiers.Shift) ? text.ToUpperInvariant() : text;
    }

    private static bool IsKittyModifierKey(int code) => code is >= 57441 and <= 57453;

    private static bool IsPrintable(Key key) => key >= Key.Space && (uint)key <= 0x10FFFF;

    private static string TextOf(uint code) => char.ConvertFromUtf32((int)code);

    /// <summary>Colon-separated codepoints; false when absent or malformed.</summary>
    private static bool TryAssociatedText(ReadOnlySpan<char> field, out string typed)
    {
        typed = "";
        while (!field.IsEmpty)
        {
            if (!NumberWhole(NextField(ref field, ':'), out var code)) return false;
            if (code <= 0 || code > 0x10FFFF) return false;
            typed += TextOf((uint)code);
        }
        return typed.Length > 0;
    }

    /// <summary>Takes the text up to the next separator and advances past it.</summary>
    private static ReadOnlySpan<char> NextField(ref ReadOnlySpan<char> text, char separator)
    {
        var index = text.IndexOf(separator);
        if (index < 0)
        {
            var last = text;
            text = default;
            return last;
        }
        var field = text[..index];
        text = text[(index + 1)..];
        return field;
    }

    /// <summary>The length of an OSC string including its ST or BEL terminator, or -1.</summary>
    private static int OscEnd(ReadOnlySpan<char> text)
    {
        for (var i = 2; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '\a' or '\u009C':
                    return i + 1;
                case '\e':
                    var isStringTerminator = i + 1 < text.Length && text[i + 1] == '\\';
                    return isStringTerminator ? i + 2 : -1;
            }
        }
        return -1;
    }

    /// <summary>The length of a string ending in <c>ESC \</c>, including it, or -1.</summary>
    private static int StringTerminatorEnd(ReadOnlySpan<char> text)
    {
        var terminator = text.IndexOf("\e\\");
        return terminator < 0 ? -1 : terminator + 2;
    }

    /// <summary>Where the paste ends, skipping over pastes nested inside its body.</summary>
    private static int PasteBodyEnd(ReadOnlySpan<char> text)
    {
        var depth = 1;
        var i = PasteStart.Length;
        while (i < text.Length)
        {
            var rest = text[i..];
            if (rest.StartsWith(PasteStart))
            {
                depth++;
                i += PasteStart.Length;
            }
            else if (rest.StartsWith(PasteEnd))
            {
                depth--;
                if (depth == 0) return i;
                i += PasteEnd.Length;
            }
            else
            {
                i++;
            }
        }
        return -1;
    }

    private static string WithoutPasteMarkers(string body) =>
        body.Replace(PasteStart, "", StringComparison.Ordinal).Replace(PasteEnd, "", StringComparison.Ordinal);

    /// <summary>Holds a short unfinished sequence and gives up on a long one.</summary>
    private static int HoldOrJunk(ReadOnlySpan<char> text) => text.Length < MaxSequence ? 0 : -text.Length;

    private static ReplyEvent Reply(ReadOnlySpan<char> raw) => new(raw.ToString());

    private static bool IsNumbersAndSemicolons(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (!char.IsAsciiDigit(c) && c != ';') return false;
        }
        return true;
    }

    private static bool IsReplyByte(char c) => char.IsAsciiDigit(c) || c is ';' or '$' or ':' or '\'';

    private static bool IsCtrlLetter(char c) => c is >= (char)1 and <= (char)0x1A;

    private static Key CtrlLetter(char c) => (Key)('A' + c - 1);

    /// <summary>Parses <c>first</c> or <c>first;second</c>.</summary>
    private static bool TryParseParameters(ReadOnlySpan<char> parameters, out int first, out int? second)
    {
        second = null;
        var semicolon = parameters.IndexOf(';');
        if (semicolon < 0) return NumberWhole(parameters, out first);

        var rest = parameters[(semicolon + 1)..];
        if (!NumberWhole(parameters[..semicolon], out first)) return false;
        if (!NumberWhole(rest, out var value)) return false;
        second = value;
        return true;
    }

    private static bool NumberWhole(ReadOnlySpan<char> text, out int value)
    {
        value = 0;
        if (text.IsEmpty || text.Length > 7) return false;
        foreach (var c in text)
        {
            if (!char.IsAsciiDigit(c)) return false;
            value = (value * 10) + (c - '0');
        }
        return true;
    }

    /// <summary>Decodes xterm's modifier parameter, <c>1 + shift + alt + ctrl</c>.</summary>
    private static KeyModifiers DecodeModifiers(int parameter) =>
        parameter is >= 2 and <= 8 ? (KeyModifiers)(parameter - 1) : KeyModifiers.None;

    private static Key LetterKey(char command) => command switch
    {
        'A' => Key.Up,
        'B' => Key.Down,
        'C' => Key.Right,
        'D' => Key.Left,
        'H' => Key.Home,
        'F' => Key.End,
        _ => Key.None,
    };

    /// <summary>A letter's key is its uppercase codepoint; anything else is its own codepoint.</summary>
    private static Key KeyOf(ReadOnlySpan<char> cluster)
    {
        var code = CodepointOf(cluster);
        var isLowercase = code is >= 'a' and <= 'z';
        return (Key)(isLowercase ? code - ('a' - 'A') : code);
    }

    private static KeyModifiers ModifiersOf(ReadOnlySpan<char> cluster) =>
        CodepointOf(cluster) is >= 'A' and <= 'Z' ? KeyModifiers.Shift : KeyModifiers.None;

    private static uint CodepointOf(ReadOnlySpan<char> cluster)
    {
        if (char.IsHighSurrogate(cluster[0]) && cluster.Length >= 2)
        {
            return (uint)char.ConvertToUtf32(cluster[0], cluster[1]);
        }
        return cluster[0];
    }

    /// <summary>Maps kitty's functional-key codepoints; other codepoints are keys as they are.</summary>
    private static Key KittyKey(int kitty) => kitty switch
    {
        27 => Key.Escape,
        9 => Key.Tab,
        13 => Key.Enter,
        127 => Key.Backspace,
        57344 or 57419 => Key.Up,
        57345 or 57420 => Key.Down,
        57346 or 57417 => Key.Left,
        57347 or 57418 => Key.Right,
        57348 or 57421 => Key.PageUp,
        57349 or 57422 => Key.PageDown,
        57350 or 57423 => Key.Home,
        57351 or 57424 => Key.End,
        57352 or 57425 => Key.Insert,
        57353 or 57426 => Key.Delete,
        >= 57364 and <= 57383 => Key.F1 + (uint)(kitty - 57364),
        > 0 and <= 0x10FFFF => (Key)(uint)kitty,
        _ => Key.None,
    };

    private static readonly Dictionary<int, Key> TildeKeys = new()
    {
        [1] = Key.Home,
        [2] = Key.Insert,
        [3] = Key.Delete,
        [4] = Key.End,
        [5] = Key.PageUp,
        [6] = Key.PageDown,
        [7] = Key.Home,
        [8] = Key.End,
        [15] = Key.F5,
        [17] = Key.F6,
        [18] = Key.F7,
        [19] = Key.F8,
        [20] = Key.F9,
        [21] = Key.F10,
        [23] = Key.F11,
        [24] = Key.F12,
    };

    private static readonly Dictionary<char, Key> Ss3Keys = new()
    {
        ['A'] = Key.Up,
        ['B'] = Key.Down,
        ['C'] = Key.Right,
        ['D'] = Key.Left,
        ['H'] = Key.Home,
        ['F'] = Key.End,
        ['P'] = Key.F1,
        ['Q'] = Key.F2,
        ['R'] = Key.F3,
        ['S'] = Key.F4,
    };
}
