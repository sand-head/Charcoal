using System.Globalization;
using SlopTui.Rendering;

namespace SlopTui.Layout;

/// <summary>
/// Applies style attributes and inline <c>style="…"</c> text to a <see cref="Style"/>.
/// Values may be typed or strings; enum names ignore case and hyphens, so
/// <c>space-between</c> and <c>SpaceBetween</c> are the same.
/// </summary>
public static class StyleParser
{
    private static readonly Dictionary<string, Func<Style, object?, Style>> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        // Layout
        ["display"] = (s, v) => s with { Display = Enum<Display>(v, "display") },
        ["position"] = (s, v) => s with { Position = Enum<Position>(v, "position") },
        ["flex-direction"] = (s, v) => s with { FlexDirection = Enum<FlexDirection>(v, "flex-direction") },
        ["justify-content"] = (s, v) => s with { JustifyContent = Enum<JustifyContent>(v, "justify-content") },
        ["align-items"] = (s, v) => s with { AlignItems = Enum<AlignItems>(v, "align-items") },
        ["align-self"] = (s, v) => s with { AlignSelf = Enum<AlignSelf>(v, "align-self") },
        ["flex-grow"] = (s, v) => s with { FlexGrow = Number(v, "flex-grow") },
        ["flex-shrink"] = (s, v) => s with { FlexShrink = Number(v, "flex-shrink") },
        ["flex-basis"] = (s, v) => s with { FlexBasis = Len(v, "flex-basis") },
        ["flex"] = Flex,
        ["width"] = (s, v) => s with { Width = Len(v, "width") },
        ["height"] = (s, v) => s with { Height = Len(v, "height") },
        ["min-width"] = (s, v) => s with { MinWidth = Len(v, "min-width") },
        ["min-height"] = (s, v) => s with { MinHeight = Len(v, "min-height") },
        ["max-width"] = (s, v) => s with { MaxWidth = Len(v, "max-width") },
        ["max-height"] = (s, v) => s with { MaxHeight = Len(v, "max-height") },
        ["padding"] = (s, v) => s with { Padding = EdgesOf(v, "padding") },
        ["padding-top"] = (s, v) => s with { Padding = s.Padding with { Top = Int(v, "padding-top") } },
        ["padding-right"] = (s, v) => s with { Padding = s.Padding with { Right = Int(v, "padding-right") } },
        ["padding-bottom"] = (s, v) => s with { Padding = s.Padding with { Bottom = Int(v, "padding-bottom") } },
        ["padding-left"] = (s, v) => s with { Padding = s.Padding with { Left = Int(v, "padding-left") } },
        ["padding-x"] = (s, v) => s with { Padding = s.Padding with { Left = Int(v, "padding-x"), Right = Int(v, "padding-x") } },
        ["padding-y"] = (s, v) => s with { Padding = s.Padding with { Top = Int(v, "padding-y"), Bottom = Int(v, "padding-y") } },
        ["margin"] = (s, v) => s with { Margin = EdgesOf(v, "margin") },
        ["margin-top"] = (s, v) => s with { Margin = s.Margin with { Top = Int(v, "margin-top") } },
        ["margin-right"] = (s, v) => s with { Margin = s.Margin with { Right = Int(v, "margin-right") } },
        ["margin-bottom"] = (s, v) => s with { Margin = s.Margin with { Bottom = Int(v, "margin-bottom") } },
        ["margin-left"] = (s, v) => s with { Margin = s.Margin with { Left = Int(v, "margin-left") } },
        ["margin-x"] = (s, v) => s with { Margin = s.Margin with { Left = Int(v, "margin-x"), Right = Int(v, "margin-x") } },
        ["margin-y"] = (s, v) => s with { Margin = s.Margin with { Top = Int(v, "margin-y"), Bottom = Int(v, "margin-y") } },
        ["gap"] = Gap,
        ["row-gap"] = (s, v) => s with { RowGap = Int(v, "row-gap") },
        ["column-gap"] = (s, v) => s with { ColumnGap = Int(v, "column-gap") },
        ["overflow"] = (s, v) => s with { Overflow = Enum<Overflow>(v, "overflow") },
        ["top"] = (s, v) => s with { Top = NullableInt(v, "top") },
        ["right"] = (s, v) => s with { Right = NullableInt(v, "right") },
        ["bottom"] = (s, v) => s with { Bottom = NullableInt(v, "bottom") },
        ["left"] = (s, v) => s with { Left = NullableInt(v, "left") },

        // Box visuals
        ["background"] = (s, v) => s with { Background = ColorOf(v, "background") },
        ["background-color"] = (s, v) => s with { Background = ColorOf(v, "background-color") },
        ["border"] = (s, v) => s with { Border = BorderOf(v) },
        ["border-style"] = (s, v) => s with { Border = BorderOf(v) },
        ["border-color"] = (s, v) => s with { BorderColor = ColorOf(v, "border-color") },
        ["border-top"] = (s, v) => s with { BorderTop = Bool(v, "border-top") },
        ["border-right"] = (s, v) => s with { BorderRight = Bool(v, "border-right") },
        ["border-bottom"] = (s, v) => s with { BorderBottom = Bool(v, "border-bottom") },
        ["border-left"] = (s, v) => s with { BorderLeft = Bool(v, "border-left") },

        // Text
        ["color"] = (s, v) => s with { Color = ColorOf(v, "color") },
        ["foreground"] = (s, v) => s with { Color = ColorOf(v, "foreground") },
        ["bold"] = (s, v) => Flag(s, TextStyle.Bold, Bool(v, "bold")),
        ["dim"] = (s, v) => Flag(s, TextStyle.Dim, Bool(v, "dim")),
        ["italic"] = (s, v) => Flag(s, TextStyle.Italic, Bool(v, "italic")),
        ["underline"] = (s, v) => Flag(s, TextStyle.Underline, Bool(v, "underline")),
        ["inverse"] = (s, v) => Flag(s, TextStyle.Inverse, Bool(v, "inverse")),
        ["strikethrough"] = (s, v) => Flag(s, TextStyle.Strikethrough, Bool(v, "strikethrough")),
        ["text-style"] = (s, v) => s with { TextStyle = TextStyleOf(v) },
        ["wrap"] = (s, v) => s with { Wrap = Enum<TextWrap>(v, "wrap") },
        ["text-wrap"] = (s, v) => s with { Wrap = Enum<TextWrap>(v, "text-wrap") },
    };

    public static bool IsStyleAttribute(string name) => Table.ContainsKey(name);

    /// <summary>
    /// Applies one property. Unknown names leave the style unchanged; a bad
    /// value throws <see cref="FormatException"/>.
    /// </summary>
    public static Style Apply(Style style, string name, object? value) =>
        Table.TryGetValue(name, out var apply) ? apply(style, value) : style;

    /// <summary>Applies <c>name: value</c> declarations in order. Unknown names throw.</summary>
    public static Style ApplyInline(Style style, string css)
    {
        foreach (var declaration in css.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = declaration.IndexOf(':');
            if (colon < 0) throw new FormatException($"'{declaration}' is not a 'name: value' declaration.");
            var name = declaration[..colon].Trim();
            var value = declaration[(colon + 1)..].Trim();
            if (!Table.TryGetValue(name, out var apply))
            {
                throw new FormatException($"'{name}' is not a style property.");
            }
            style = apply(style, value);
        }
        return style;
    }

    private static string Text(object? value) => value switch
    {
        null => "",
        string s => s.Trim(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    private static T Enum<T>(object? value, string name) where T : struct, Enum
    {
        if (value is T typed) return typed;
        var text = Text(value).Replace("-", "").Replace("_", "");
        if (System.Enum.TryParse<T>(text, ignoreCase: true, out var parsed)) return parsed;
        throw Bad(name, value);
    }

    private static double Number(object? value, string name)
    {
        switch (value)
        {
            case double d: return d;
            case float f: return f;
            case int i: return i;
            case long l: return l;
            case decimal m: return (double)m;
        }
        if (double.TryParse(Text(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        throw Bad(name, value);
    }

    private static int Int(object? value, string name)
    {
        switch (value)
        {
            case int i: return i;
            case long l: return checked((int)l);
            case double d: return (int)Math.Round(d);
        }
        if (int.TryParse(Text(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        throw Bad(name, value);
    }

    private static int? NullableInt(object? value, string name)
    {
        if (value is null) return null;
        var text = Text(value);
        if (text.Length == 0 || text.Equals("auto", StringComparison.OrdinalIgnoreCase)) return null;
        return Int(value, name);
    }

    private static bool Bool(object? value, string name) => value switch
    {
        bool b => b,
        null => true,
        _ => Text(value).ToLowerInvariant() switch
        {
            "" or "true" or "yes" or "on" => true,
            "false" or "no" or "off" or "none" => false,
            _ => throw Bad(name, value),
        },
    };

    private static Length Len(object? value, string name)
    {
        switch (value)
        {
            case Length l: return l;
            case int i: return Length.Cells(i);
            case long l: return Length.Cells(checked((int)l));
            case double d: return Length.Cells((int)Math.Round(d));
        }
        var text = Text(value);
        if (text.Length == 0 || text.Equals("auto", StringComparison.OrdinalIgnoreCase)) return Length.Auto;
        if (text.EndsWith('%') && int.TryParse(text[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent))
        {
            return Length.Percent(percent);
        }
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cells)) return Length.Cells(cells);
        throw Bad(name, value);
    }

    /// <summary>One value for all sides, two for vertical and horizontal, or four clockwise from the top.</summary>
    private static Edges EdgesOf(object? value, string name)
    {
        switch (value)
        {
            case Edges e: return e;
            case int i: return new Edges(i);
        }
        var parts = Text(value).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[i]))
            {
                throw Bad(name, value);
            }
        }
        return numbers.Length switch
        {
            1 => new Edges(numbers[0]),
            2 => new Edges(numbers[0], numbers[1]),
            4 => new Edges(numbers[0], numbers[1], numbers[2], numbers[3]),
            _ => throw Bad(name, value),
        };
    }

    private static Color ColorOf(object? value, string name)
    {
        if (value is Color c) return c;
        if (value is null) return Color.Default;
        if (Color.TryParse(Text(value), out var parsed)) return parsed;
        throw Bad(name, value);
    }

    private static BorderStyle BorderOf(object? value)
    {
        if (value is BorderStyle b) return b;
        if (value is bool on) return on ? BorderStyle.Single : BorderStyle.None;
        var text = Text(value).ToLowerInvariant();
        return text switch
        {
            "" or "true" => BorderStyle.Single,
            "false" => BorderStyle.None,
            _ => Enum<BorderStyle>(text, "border"),
        };
    }

    private static TextStyle TextStyleOf(object? value)
    {
        if (value is TextStyle t) return t;
        var result = TextStyle.None;
        foreach (var part in Text(value).Split([' ', ',', '|'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
            result |= Enum<TextStyle>(part, "text-style");
        }
        return result;
    }

    private static Style Gap(Style style, object? value)
    {
        var gap = Int(value, "gap");
        return style with { RowGap = gap, ColumnGap = gap };
    }

    private static Style Flag(Style style, TextStyle flag, bool on) =>
        style with { TextStyle = on ? style.TextStyle | flag : style.TextStyle & ~flag };

    /// <summary>The <c>flex</c> shorthand: <c>grow [shrink] [basis]</c>, <c>none</c> or <c>auto</c>.</summary>
    private static Style Flex(Style style, object? value)
    {
        if (value is int or double or long or float)
        {
            return style with { FlexGrow = Number(value, "flex"), FlexShrink = 1, FlexBasis = Length.Cells(0) };
        }
        var text = Text(value).ToLowerInvariant();
        if (text == "none") return style with { FlexGrow = 0, FlexShrink = 0, FlexBasis = Length.Auto };
        if (text == "auto") return style with { FlexGrow = 1, FlexShrink = 1, FlexBasis = Length.Auto };
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 3) throw Bad("flex", value);
        var grow = Number(parts[0], "flex");
        var shrink = parts.Length > 1 ? Number(parts[1], "flex") : 1;
        var basis = parts.Length > 2 ? Len(parts[2], "flex") : Length.Cells(0);
        return style with { FlexGrow = grow, FlexShrink = shrink, FlexBasis = basis };
    }

    private static FormatException Bad(string name, object? value) =>
        new($"'{value}' is not a valid value for '{name}'.");
}
