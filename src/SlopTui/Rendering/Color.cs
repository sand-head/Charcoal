using System.Globalization;

namespace SlopTui.Rendering;

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
    /// Parses <c>default</c>, an ANSI name such as <c>red</c> or <c>bright-blue</c>,
    /// <c>#rgb</c>, <c>#rrggbb</c>, <c>rgb(r, g, b)</c> or <c>ansi(n)</c>.
    /// </summary>
    public static bool TryParse(string? text, out Color color)
    {
        color = Default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().ToLowerInvariant();

        if (s is "default" or "inherit" or "none") return true;
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
        if (index < 0) return false;

        color = Ansi(index);
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

    private static bool TryParseRgb(string arguments, out Color color)
    {
        color = Default;
        var parts = arguments.Split(',', StringSplitOptions.TrimEntries);
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
