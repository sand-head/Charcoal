using System.Globalization;

namespace Charcoal.Rendering;

/// <summary>How a <see cref="Color"/> is expressed to the terminal.</summary>
public enum ColorKind : byte
{
    /// <summary>The terminal's own foreground or background; SGR 39 / 49.</summary>
    Default,
    /// <summary>One of the sixteen ANSI colours; <see cref="Color.Index"/> is 0–15.</summary>
    Ansi,
    /// <summary>The 256-colour palette; <see cref="Color.Index"/> is 0–255.</summary>
    Indexed,
    /// <summary>24-bit; <see cref="Color.R"/>, <see cref="Color.G"/>, <see cref="Color.B"/>.</summary>
    Rgb,
}

/// <summary>A terminal colour: the default, an ANSI colour, a palette index, or RGB.</summary>
public readonly record struct Color(ColorKind Kind, byte Index, byte R, byte G, byte B)
{
    public static readonly Color Default = new(ColorKind.Default, 0, 0, 0, 0);

    public static Color Ansi(int index) => new(ColorKind.Ansi, checked((byte)index), 0, 0, 0);
    public static Color Indexed(int index) => new(ColorKind.Indexed, checked((byte)index), 0, 0, 0);
    public static Color Rgb(int r, int g, int b) => new(ColorKind.Rgb, 0, checked((byte)r), checked((byte)g), checked((byte)b));

    public static readonly Color Black = Ansi(0);
    public static readonly Color Red = Ansi(1);
    public static readonly Color Green = Ansi(2);
    public static readonly Color Yellow = Ansi(3);
    public static readonly Color Blue = Ansi(4);
    public static readonly Color Magenta = Ansi(5);
    public static readonly Color Cyan = Ansi(6);
    public static readonly Color White = Ansi(7);
    public static readonly Color BrightBlack = Ansi(8);
    public static readonly Color BrightRed = Ansi(9);
    public static readonly Color BrightGreen = Ansi(10);
    public static readonly Color BrightYellow = Ansi(11);
    public static readonly Color BrightBlue = Ansi(12);
    public static readonly Color BrightMagenta = Ansi(13);
    public static readonly Color BrightCyan = Ansi(14);
    public static readonly Color BrightWhite = Ansi(15);

    private static readonly string[] AnsiNames =
    [
        "black", "red", "green", "yellow", "blue", "magenta", "cyan", "white",
        "bright-black", "bright-red", "bright-green", "bright-yellow",
        "bright-blue", "bright-magenta", "bright-cyan", "bright-white",
    ];

    /// <summary>
    /// CSS named colours other than the sixteen palette names, as sRGB. The
    /// palette names follow the terminal's theme instead.
    /// </summary>
    private static readonly Dictionary<string, int> CssNames = new(StringComparer.Ordinal)
    {
        ["aliceblue"] = 0xF0F8FF, ["antiquewhite"] = 0xFAEBD7, ["aqua"] = 0x00FFFF, ["aquamarine"] = 0x7FFFD4, ["azure"] = 0xF0FFFF,
        ["beige"] = 0xF5F5DC, ["bisque"] = 0xFFE4C4, ["blanchedalmond"] = 0xFFEBCD, ["blueviolet"] = 0x8A2BE2, ["brown"] = 0xA52A2A,
        ["burlywood"] = 0xDEB887, ["cadetblue"] = 0x5F9EA0, ["chartreuse"] = 0x7FFF00, ["chocolate"] = 0xD2691E, ["coral"] = 0xFF7F50,
        ["cornflowerblue"] = 0x6495ED, ["cornsilk"] = 0xFFF8DC, ["crimson"] = 0xDC143C, ["darkblue"] = 0x00008B, ["darkcyan"] = 0x008B8B,
        ["darkgoldenrod"] = 0xB8860B, ["darkgray"] = 0xA9A9A9, ["darkgrey"] = 0xA9A9A9, ["darkgreen"] = 0x006400, ["darkkhaki"] = 0xBDB76B,
        ["darkmagenta"] = 0x8B008B, ["darkolivegreen"] = 0x556B2F, ["darkorange"] = 0xFF8C00, ["darkorchid"] = 0x9932CC, ["darkred"] = 0x8B0000,
        ["darksalmon"] = 0xE9967A, ["darkseagreen"] = 0x8FBC8F, ["darkslateblue"] = 0x483D8B, ["darkslategray"] = 0x2F4F4F, ["darkslategrey"] = 0x2F4F4F,
        ["darkturquoise"] = 0x00CED1, ["darkviolet"] = 0x9400D3, ["deeppink"] = 0xFF1493, ["deepskyblue"] = 0x00BFFF, ["dimgray"] = 0x696969,
        ["dimgrey"] = 0x696969, ["dodgerblue"] = 0x1E90FF, ["firebrick"] = 0xB22222, ["floralwhite"] = 0xFFFAF0, ["forestgreen"] = 0x228B22,
        ["fuchsia"] = 0xFF00FF, ["gainsboro"] = 0xDCDCDC, ["ghostwhite"] = 0xF8F8FF, ["gold"] = 0xFFD700, ["goldenrod"] = 0xDAA520,
        ["greenyellow"] = 0xADFF2F, ["honeydew"] = 0xF0FFF0, ["hotpink"] = 0xFF69B4, ["indianred"] = 0xCD5C5C, ["indigo"] = 0x4B0082,
        ["ivory"] = 0xFFFFF0, ["khaki"] = 0xF0E68C, ["lavender"] = 0xE6E6FA, ["lavenderblush"] = 0xFFF0F5, ["lawngreen"] = 0x7CFC00,
        ["lemonchiffon"] = 0xFFFACD, ["lightblue"] = 0xADD8E6, ["lightcoral"] = 0xF08080, ["lightcyan"] = 0xE0FFFF, ["lightgoldenrodyellow"] = 0xFAFAD2,
        ["lightgray"] = 0xD3D3D3, ["lightgrey"] = 0xD3D3D3, ["lightgreen"] = 0x90EE90, ["lightpink"] = 0xFFB6C1, ["lightsalmon"] = 0xFFA07A,
        ["lightseagreen"] = 0x20B2AA, ["lightskyblue"] = 0x87CEFA, ["lightslategray"] = 0x778899, ["lightslategrey"] = 0x778899, ["lightsteelblue"] = 0xB0C4DE,
        ["lightyellow"] = 0xFFFFE0, ["lime"] = 0x00FF00, ["limegreen"] = 0x32CD32, ["linen"] = 0xFAF0E6, ["maroon"] = 0x800000,
        ["mediumaquamarine"] = 0x66CDAA, ["mediumblue"] = 0x0000CD, ["mediumorchid"] = 0xBA55D3, ["mediumpurple"] = 0x9370DB, ["mediumseagreen"] = 0x3CB371,
        ["mediumslateblue"] = 0x7B68EE, ["mediumspringgreen"] = 0x00FA9A, ["mediumturquoise"] = 0x48D1CC, ["mediumvioletred"] = 0xC71585, ["midnightblue"] = 0x191970,
        ["mintcream"] = 0xF5FFFA, ["mistyrose"] = 0xFFE4E1, ["moccasin"] = 0xFFE4B5, ["navajowhite"] = 0xFFDEAD, ["navy"] = 0x000080,
        ["oldlace"] = 0xFDF5E6, ["olive"] = 0x808000, ["olivedrab"] = 0x6B8E23, ["orange"] = 0xFFA500, ["orangered"] = 0xFF4500,
        ["orchid"] = 0xDA70D6, ["palegoldenrod"] = 0xEEE8AA, ["palegreen"] = 0x98FB98, ["paleturquoise"] = 0xAFEEEE, ["palevioletred"] = 0xDB7093,
        ["papayawhip"] = 0xFFEFD5, ["peachpuff"] = 0xFFDAB9, ["peru"] = 0xCD853F, ["pink"] = 0xFFC0CB, ["plum"] = 0xDDA0DD,
        ["powderblue"] = 0xB0E0E6, ["purple"] = 0x800080, ["rebeccapurple"] = 0x663399, ["rosybrown"] = 0xBC8F8F, ["royalblue"] = 0x4169E1,
        ["saddlebrown"] = 0x8B4513, ["salmon"] = 0xFA8072, ["sandybrown"] = 0xF4A460, ["seagreen"] = 0x2E8B57, ["seashell"] = 0xFFF5EE,
        ["sienna"] = 0xA0522D, ["silver"] = 0xC0C0C0, ["skyblue"] = 0x87CEEB, ["slateblue"] = 0x6A5ACD, ["slategray"] = 0x708090,
        ["slategrey"] = 0x708090, ["snow"] = 0xFFFAFA, ["springgreen"] = 0x00FF7F, ["steelblue"] = 0x4682B4, ["tan"] = 0xD2B48C,
        ["teal"] = 0x008080, ["thistle"] = 0xD8BFD8, ["tomato"] = 0xFF6347, ["turquoise"] = 0x40E0D0, ["violet"] = 0xEE82EE,
        ["wheat"] = 0xF5DEB3, ["whitesmoke"] = 0xF5F5F5, ["yellowgreen"] = 0x9ACD32,
    };

    /// <summary>
    /// Parses a CSS colour: a named colour, <c>#rgb</c>, <c>#rrggbb</c>,
    /// <c>rgb(r, g, b)</c>, <c>ansi(n)</c> for a palette index, or
    /// <c>currentcolor</c>, <c>transparent</c> or <c>default</c> for the
    /// terminal's own colour.
    /// </summary>
    public static bool TryParse(string? text, out Color color)
    {
        color = Default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().ToLowerInvariant();

        if (s is "default" or "inherit" or "none" or "transparent" or "currentcolor" or "initial" or "unset") return true;
        if (s.StartsWith('#')) return TryParseHex(s[1..], out color);
        if (s.StartsWith("rgb(") && s.EndsWith(')')) return TryParseRgb(s[4..^1], out color);
        if ((s.StartsWith("ansi(") || s.StartsWith("index(")) && s.EndsWith(')')) return TryParseIndex(s, out color);
        return TryParseName(s, out color);
    }

    private static bool TryParseName(string name, out Color color)
    {
        color = Default;
        if (name is "gray" or "grey")
        {
            color = BrightBlack;
            return true;
        }

        var index = Array.IndexOf(AnsiNames, name);
        if (index < 0)
        {
            index = Array.IndexOf(AnsiNames, name.Replace("bright", "bright-"));
        }
        if (index >= 0)
        {
            color = Ansi(index);
            return true;
        }

        if (!CssNames.TryGetValue(name, out var rgb)) return false;
        color = Rgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        return true;
    }

    private static bool TryParseHex(string hex, out Color color)
    {
        color = Default;
        if (hex.Length == 3)
        {
            hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        }
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        color = Rgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        return true;
    }

    /// <summary>Reads <c>r, g, b</c> or <c>r g b / alpha</c>, ignoring the alpha a terminal cannot draw.</summary>
    private static bool TryParseRgb(string arguments, out Color color)
    {
        color = Default;
        var parts = arguments.Replace('/', ' ').Split([',', ' '], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 4) parts = parts[..3];
        if (parts.Length != 3) return false;
        if (!byte.TryParse(parts[0], out var r) || !byte.TryParse(parts[1], out var g) || !byte.TryParse(parts[2], out var b))
        {
            return false;
        }

        color = Rgb(r, g, b);
        return true;
    }

    private static bool TryParseIndex(string function, out Color color)
    {
        color = Default;
        var argument = function[(function.IndexOf('(') + 1)..^1];
        if (!byte.TryParse(argument, out var index)) return false;

        color = index < 16 ? Ansi(index) : Indexed(index);
        return true;
    }

    public static Color Parse(string text) =>
        TryParse(text, out var color) ? color : throw new FormatException($"'{text}' is not a colour.");

    /// <summary>The SGR parameters that select this colour as the foreground or background.</summary>
    public string ToSgr(bool foreground) => Kind switch
    {
        ColorKind.Default => foreground ? "39" : "49",
        ColorKind.Ansi when Index < 8 => ((foreground ? 30 : 40) + Index).ToString(),
        ColorKind.Ansi => ((foreground ? 90 : 100) + Index - 8).ToString(),
        ColorKind.Indexed => $"{(foreground ? 38 : 48)};5;{Index}",
        _ => $"{(foreground ? 38 : 48)};2;{R};{G};{B}",
    };

    public override string ToString() => Kind switch
    {
        ColorKind.Default => "default",
        ColorKind.Ansi => AnsiNames[Index],
        ColorKind.Indexed => $"ansi({Index})",
        _ => $"#{R:x2}{G:x2}{B:x2}",
    };
}
