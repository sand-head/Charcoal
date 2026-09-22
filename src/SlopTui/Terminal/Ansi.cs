namespace SlopTui.Terminal;

/// <summary>The escape sequences shared by the terminal runtime and the renderer.</summary>
/// <remarks>
/// Mouse reporting uses button-motion (1002) rather than any-motion (1003),
/// which would report every movement. The SGR encoding (1006) is enabled last
/// and disabled first.
/// </remarks>
public static class Ansi
{
    public const string Esc = "\e";
    public const string Csi = "\e[";

    // Screen
    public const string AlternateScreenOn = "\e[?1049h";
    public const string AlternateScreenOff = "\e[?1049l";
    public const string ClearScreen = "\e[2J";
    public const string ResetAttributes = "\e[0m";

    // Cursor
    public const string HideCursor = "\e[?25l";
    public const string ShowCursor = "\e[?25h";
    public const string CursorHome = "\e[H";

    /// <summary>Moves the cursor to a zero-based cell.</summary>
    public static string CursorPosition(int x, int y) => $"\e[{y + 1};{x + 1}H";

    // Mouse
    public const string MouseOn = "\e[?1002h\e[?1006h";
    public const string MouseOff = "\e[?1006l\e[?1002l";

    /// <summary>Reports every movement, for hover.</summary>
    public const string MouseAnyMotionOn = "\e[?1003h\e[?1006h";
    public const string MouseAnyMotionOff = "\e[?1006l\e[?1003l";

    // Paste and focus
    public const string BracketedPasteOn = "\e[?2004h";
    public const string BracketedPasteOff = "\e[?2004l";
    public const string FocusEventsOn = "\e[?1004h";
    public const string FocusEventsOff = "\e[?1004l";

    // Kitty keyboard protocol, flag 1: disambiguate escape codes.
    public const string KittyKeyboardPush = "\e[>1u";
    public const string KittyKeyboardPop = "\e[<u";

    // Synchronized output (DEC 2026)
    public const string SynchronizedOutputBegin = "\e[?2026h";
    public const string SynchronizedOutputEnd = "\e[?2026l";

    // Queries
    public const string QuerySynchronizedOutput = "\e[?2026$p";
    /// <summary>Primary device attributes. Every terminal answers, so the reply marks the end of the others.</summary>
    public const string QueryDeviceAttributes = "\e[c";
    public const string QueryCursorPosition = "\e[6n";
    public const string QueryKittyKeyboard = "\e[?u";

    /// <summary>XTWINOPS 16, the cell size in pixels. The reply is <c>CSI 6 ; height ; width t</c>.</summary>
    public const string QueryCellPixels = "\e[16t";

    /// <summary>OSC 11, the default background colour. The reply is <c>OSC 11 ; rgb:rrrr/gggg/bbbb ST</c>.</summary>
    public const string QueryBackground = "\e]11;?\e\\";

    public const string EraseToEndOfLine = "\e[K";
}
