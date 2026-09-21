namespace SlopTui.Input;

/// <summary>Parses SGR (mode 1006) mouse reports.</summary>
public static class MouseInput
{
    /// <summary>Finds the first <c>ESC [ &lt; b ; x ; y M|m</c> report in the text.</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out MouseEvent? mouse, out int start, out int length)
    {
        mouse = null;
        start = -1;
        length = 0;

        for (var i = 0; i + 5 < text.Length; i++)
        {
            if (text[i] != '\e' || text[i + 1] != '[' || text[i + 2] != '<') continue;

            var cursor = i + 3;
            if (!Number(text, ref cursor, out var code)) continue;
            if (cursor >= text.Length || text[cursor++] != ';') continue;
            if (!Number(text, ref cursor, out var column)) continue;
            if (cursor >= text.Length || text[cursor++] != ';') continue;
            if (!Number(text, ref cursor, out var row)) continue;
            if (cursor >= text.Length) continue;

            var final = text[cursor];
            if (final is not ('M' or 'm')) continue;

            mouse = Decode(code, column, row, released: final == 'm');
            start = i;
            length = cursor + 1 - i;
            return true;
        }

        return false;
    }

    /// <summary>Whether the text ends in the unfinished start of a report.</summary>
    public static bool CouldBePartial(ReadOnlySpan<char> text)
    {
        var escape = text.LastIndexOf('\e');
        if (escape < 0) return false;

        const string introducer = "\e[<";
        var tail = text[escape..];
        var introducerPart = tail[..Math.Min(tail.Length, introducer.Length)];
        if (!introducer.AsSpan().StartsWith(introducerPart)) return false;

        foreach (var c in tail[introducerPart.Length..])
        {
            if (!char.IsAsciiDigit(c) && c != ';') return false;
        }
        return true;
    }

    private static MouseEvent Decode(int code, int column, int row, bool released)
    {
        var modifiers = KeyModifiers.None;
        if ((code & 4) != 0) modifiers |= KeyModifiers.Shift;
        if ((code & 8) != 0) modifiers |= KeyModifiers.Alt;
        if ((code & 16) != 0) modifiers |= KeyModifiers.Ctrl;

        var x = Math.Max(0, column - 1);
        var y = Math.Max(0, row - 1);

        var isWheel = (code & 64) != 0;
        if (isWheel)
        {
            var direction = (code & 1) == 0 ? MouseAction.WheelUp : MouseAction.WheelDown;
            return new MouseEvent(direction, MouseButton.None, x, y, modifiers);
        }

        var button = (code & 3) switch
        {
            0 => MouseButton.Left,
            1 => MouseButton.Middle,
            2 => MouseButton.Right,
            _ => MouseButton.None,
        };

        var isMotion = (code & 32) != 0;
        if (isMotion) return new MouseEvent(MouseAction.Moved, button, x, y, modifiers);

        var action = released ? MouseAction.Released : MouseAction.Pressed;
        return new MouseEvent(action, button, x, y, modifiers);
    }

    private static bool Number(ReadOnlySpan<char> text, ref int cursor, out int value)
    {
        value = 0;
        var digits = 0;
        while (cursor < text.Length && char.IsAsciiDigit(text[cursor]))
        {
            value = (value * 10) + (text[cursor] - '0');
            cursor++;
            if (++digits > 6) return false;
        }
        return digits > 0;
    }
}
