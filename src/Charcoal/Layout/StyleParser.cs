using System.Globalization;
using System.Text;
using Charcoal.Rendering;

namespace Charcoal.Layout;

/// <summary>
/// Applies CSS declarations to a <see cref="Style"/>, one property at a time
/// or a whole <c>style="…"</c> string.
/// </summary>
/// <remarks>
/// <para>
/// Names and values are CSS's, with the cell as the unit: <c>padding: 1 2</c>,
/// <c>width: 50%</c>, <c>border: solid cyan</c>. Lengths may use <c>ch</c>,
/// <c>em</c>, <c>rem</c> or <c>lh</c>, which all mean cells; <c>px</c> is
/// refused because a terminal has no pixels.
/// </para>
/// <para>
/// A bad value throws, naming the property. Properties a terminal cannot draw,
/// such as <c>font-family</c>, are accepted and ignored so a page's stylesheet
/// still loads.
/// </para>
/// </remarks>
public static class StyleParser
{
    private static readonly Dictionary<string, Func<Style, object?, Style>> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        // Layout
        ["display"] = (s, v) => s with { Display = DisplayOf(v) },
        ["position"] = (s, v) => s with { Position = Enum<Position>(v, "position") },
        ["visibility"] = (s, v) => s with { Visibility = VisibilityOf(v) },
        ["flex-direction"] = (s, v) => s with { FlexDirection = Enum<FlexDirection>(v, "flex-direction") },
        ["flex-wrap"] = (s, v) => s with { FlexWrap = Enum<FlexWrap>(v, "flex-wrap") },
        ["flex-flow"] = FlexFlow,
        ["justify-content"] = (s, v) => s with { JustifyContent = JustifyOf(v) },
        ["align-items"] = (s, v) => s with { AlignItems = AlignItemsOf(v, "align-items") },
        ["align-self"] = (s, v) => s with { AlignSelf = AlignSelfOf(v) },
        ["align-content"] = (s, v) => s with { AlignContent = AlignContentOf(v) },
        ["justify-items"] = (s, v) => s with { JustifyItems = AlignItemsOf(v, "justify-items") },
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
        ["padding-top"] = (s, v) => s with { Padding = s.Padding with { Top = Cells(v, "padding-top") } },
        ["padding-right"] = (s, v) => s with { Padding = s.Padding with { Right = Cells(v, "padding-right") } },
        ["padding-bottom"] = (s, v) => s with { Padding = s.Padding with { Bottom = Cells(v, "padding-bottom") } },
        ["padding-left"] = (s, v) => s with { Padding = s.Padding with { Left = Cells(v, "padding-left") } },
        ["padding-inline"] = (s, v) => s with { Padding = WithInline(s.Padding, Pair(v, "padding-inline")) },
        ["padding-block"] = (s, v) => s with { Padding = WithBlock(s.Padding, Pair(v, "padding-block")) },
        ["padding-inline-start"] = (s, v) => s with { Padding = s.Padding with { Left = Cells(v, "padding-inline-start") } },
        ["padding-inline-end"] = (s, v) => s with { Padding = s.Padding with { Right = Cells(v, "padding-inline-end") } },
        ["padding-block-start"] = (s, v) => s with { Padding = s.Padding with { Top = Cells(v, "padding-block-start") } },
        ["padding-block-end"] = (s, v) => s with { Padding = s.Padding with { Bottom = Cells(v, "padding-block-end") } },
        ["margin"] = (s, v) => s with { Margin = EdgesOf(v, "margin") },
        ["margin-top"] = (s, v) => s with { Margin = s.Margin with { Top = Cells(v, "margin-top") } },
        ["margin-right"] = (s, v) => s with { Margin = s.Margin with { Right = Cells(v, "margin-right") } },
        ["margin-bottom"] = (s, v) => s with { Margin = s.Margin with { Bottom = Cells(v, "margin-bottom") } },
        ["margin-left"] = (s, v) => s with { Margin = s.Margin with { Left = Cells(v, "margin-left") } },
        ["margin-inline"] = (s, v) => s with { Margin = WithInline(s.Margin, Pair(v, "margin-inline")) },
        ["margin-block"] = (s, v) => s with { Margin = WithBlock(s.Margin, Pair(v, "margin-block")) },
        ["margin-inline-start"] = (s, v) => s with { Margin = s.Margin with { Left = Cells(v, "margin-inline-start") } },
        ["margin-inline-end"] = (s, v) => s with { Margin = s.Margin with { Right = Cells(v, "margin-inline-end") } },
        ["margin-block-start"] = (s, v) => s with { Margin = s.Margin with { Top = Cells(v, "margin-block-start") } },
        ["margin-block-end"] = (s, v) => s with { Margin = s.Margin with { Bottom = Cells(v, "margin-block-end") } },
        ["gap"] = Gap,
        ["row-gap"] = (s, v) => s with { RowGap = Cells(v, "row-gap") },
        ["column-gap"] = (s, v) => s with { ColumnGap = Cells(v, "column-gap") },
        ["overflow"] = (s, v) => s with { Overflow = OverflowOf(v, "overflow") },
        ["overflow-anchor"] = (s, v) => s with { OverflowAnchor = OverflowAnchorOf(v) },
        ["overflow-x"] = (s, v) => s with { Overflow = OverflowOf(v, "overflow-x") },
        ["overflow-y"] = (s, v) => s with { Overflow = OverflowOf(v, "overflow-y") },
        ["user-select"] = (s, v) => s with { UserSelect = UserSelectOf(v) },
        ["container-type"] = (s, v) => s with { ContainerType = ContainerTypeOf(v) },
        ["container-name"] = (s, v) => s with { ContainerNames = ContainerNamesOf(v) },
        ["container"] = Container,
        ["top"] = (s, v) => s with { Top = NullableCells(v, "top") },
        ["right"] = (s, v) => s with { Right = NullableCells(v, "right") },
        ["bottom"] = (s, v) => s with { Bottom = NullableCells(v, "bottom") },
        ["left"] = (s, v) => s with { Left = NullableCells(v, "left") },

        // Grid
        ["grid-template-columns"] = (s, v) => s with { GridTemplateColumns = Tracks(v, "grid-template-columns") },
        ["grid-template-rows"] = (s, v) => s with { GridTemplateRows = Tracks(v, "grid-template-rows") },
        ["grid-column"] = GridColumn,
        ["grid-row"] = GridRow,
        ["grid-column-start"] = (s, v) => s with { GridColumnStart = NullableCells(v, "grid-column-start") },
        ["grid-row-start"] = (s, v) => s with { GridRowStart = NullableCells(v, "grid-row-start") },
        ["grid-column-end"] = (s, v) => s with { GridColumnSpan = SpanFromEnd(v, s.GridColumnStart, "grid-column-end") },
        ["grid-row-end"] = (s, v) => s with { GridRowSpan = SpanFromEnd(v, s.GridRowStart, "grid-row-end") },

        // Box visuals
        ["background"] = (s, v) => s with { Background = ColorOf(v, "background") },
        ["background-color"] = (s, v) => s with { Background = ColorOf(v, "background-color") },
        ["border"] = (s, v) => Border(s, v, null),
        ["border-top"] = (s, v) => Border(s, v, Side.Top),
        ["border-right"] = (s, v) => Border(s, v, Side.Right),
        ["border-bottom"] = (s, v) => Border(s, v, Side.Bottom),
        ["border-left"] = (s, v) => Border(s, v, Side.Left),
        ["border-style"] = (s, v) => WithSideStyle(s, BorderStyleOf(First(v), "border-style"), null),
        ["border-top-style"] = (s, v) => s with { BorderTopStyle = BorderStyleOf(v, "border-top-style") },
        ["border-right-style"] = (s, v) => s with { BorderRightStyle = BorderStyleOf(v, "border-right-style") },
        ["border-bottom-style"] = (s, v) => s with { BorderBottomStyle = BorderStyleOf(v, "border-bottom-style") },
        ["border-left-style"] = (s, v) => s with { BorderLeftStyle = BorderStyleOf(v, "border-left-style") },
        ["border-width"] = (s, v) => s with { BorderWidth = BorderWidthOf(v, "border-width") },
        ["border-top-width"] = (s, v) => s with { BorderWidth = BorderWidthOf(v, "border-top-width") },
        ["border-right-width"] = (s, v) => s with { BorderWidth = BorderWidthOf(v, "border-right-width") },
        ["border-bottom-width"] = (s, v) => s with { BorderWidth = BorderWidthOf(v, "border-bottom-width") },
        ["border-left-width"] = (s, v) => s with { BorderWidth = BorderWidthOf(v, "border-left-width") },
        ["border-color"] = (s, v) => s with { BorderColor = ColorOf(First(v), "border-color") },
        ["border-top-color"] = (s, v) => s with { BorderColor = ColorOf(v, "border-top-color") },
        ["border-right-color"] = (s, v) => s with { BorderColor = ColorOf(v, "border-right-color") },
        ["border-bottom-color"] = (s, v) => s with { BorderColor = ColorOf(v, "border-bottom-color") },
        ["border-left-color"] = (s, v) => s with { BorderColor = ColorOf(v, "border-left-color") },
        ["border-radius"] = (s, v) => s with { BorderRadius = Cells(First(v), "border-radius") },

        // Text
        ["color"] = TextColor,
        ["font-weight"] = FontWeight,
        ["font-style"] = FontStyle,
        ["text-decoration"] = TextDecoration,
        ["text-decoration-line"] = TextDecoration,
        ["opacity"] = Opacity,
        ["filter"] = Filter,
        ["white-space"] = (s, v) => s with { WhiteSpace = WhiteSpaceOf(v), Set = s.Set | StyleSet.WhiteSpace },
        ["text-overflow"] = (s, v) => s with { TextOverflow = TextOverflowOf(v) },
        ["text-align"] = (s, v) => s with { TextAlign = TextAlignOf(v), Set = s.Set | StyleSet.TextAlign },
    };

    /// <summary>CSS properties a terminal cannot draw, accepted as no-ops.</summary>
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "font", "font-family", "font-size", "font-variant", "font-stretch", "line-height", "letter-spacing", "word-spacing",
        "text-transform", "text-indent", "text-shadow", "text-decoration-color", "text-decoration-style", "text-decoration-thickness",
        "text-underline-offset", "vertical-align", "word-break", "overflow-wrap", "word-wrap", "hyphens", "tab-size",
        "z-index", "cursor", "box-shadow", "box-sizing", "outline", "outline-color", "outline-style", "outline-width", "outline-offset",
        "transition", "transition-property", "transition-duration", "transition-timing-function", "transition-delay",
        "animation", "animation-name", "animation-duration", "transform", "transform-origin", "background-image", "background-size",
        "background-position", "background-repeat", "background-attachment", "background-clip", "border-collapse", "border-spacing",
        "border-image", "list-style", "list-style-type", "list-style-position", "pointer-events", "appearance",
        "resize", "content", "quotes", "scroll-behavior", "scrollbar-width", "scrollbar-color", "will-change", "isolation",
        "mix-blend-mode", "backdrop-filter", "clip-path", "object-fit", "object-position", "aspect-ratio", "inset", "order",
        "grid-area", "grid-template-areas", "grid-template", "grid-auto-flow", "grid-auto-rows", "grid-auto-columns", "place-items",
        "place-content", "place-self", "justify-self", "float", "clear", "columns", "column-count", "column-width",
        "-webkit-font-smoothing", "-moz-osx-font-smoothing", "text-rendering", "font-feature-settings", "direction", "unicode-bidi",
        "writing-mode", "caret-color", "accent-color", "color-scheme", "forced-color-adjust", "print-color-adjust",
    };

    private static readonly string[] CellUnits = ["rem", "cap", "ch", "em", "lh", "ex", "ic"];

    public static bool IsProperty(string name) => Table.ContainsKey(name);

    /// <summary>Whether the name is a CSS property that is accepted but not drawn.</summary>
    public static bool IsIgnored(string name) => Ignored.Contains(name);

    /// <summary>Whether the name is a custom property such as <c>--accent</c>.</summary>
    public static bool IsCustomProperty(string name) => name.StartsWith("--", StringComparison.Ordinal) && name.Length > 2;

    /// <summary>Applies one property. Ignored and custom properties leave the style unchanged.</summary>
    /// <exception cref="FormatException">The name is unknown or the value does not parse.</exception>
    public static Style Apply(Style style, string name, object? value)
    {
        if (Table.TryGetValue(name, out var apply)) return apply(style, value);
        if (Ignored.Contains(name) || IsCustomProperty(name)) return style;
        throw new FormatException($"'{name}' is not a style property.");
    }

    /// <summary>
    /// Applies the declarations of a <c>style="…"</c> string in order. Its own
    /// custom properties are visible to its <c>var()</c>s, and
    /// <paramref name="variables"/> supplies the rest.
    /// </summary>
    public static Style ApplyInline(Style style, string css, IReadOnlyDictionary<string, string>? variables = null)
    {
        var declarations = ParseDeclarations(css);
        var own = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in declarations)
        {
            if (IsCustomProperty(name)) own[name] = value;
        }

        string? Lookup(string key) => own.TryGetValue(key, out var value) ? value : variables?.GetValueOrDefault(key);
        foreach (var (name, value) in declarations)
        {
            if (IsCustomProperty(name)) continue;
            if (Substitute(value, Lookup) is { } resolved) style = Apply(style, name, resolved);
        }
        return style;
    }

    /// <summary>The <c>name: value</c> pairs of a declaration list, with <c>!important</c> dropped.</summary>
    /// <exception cref="FormatException">A part is not a declaration.</exception>
    public static List<(string Name, string Value)> ParseDeclarations(string css)
    {
        var result = new List<(string, string)>();
        foreach (var declaration in SplitDeclarations(css))
        {
            var colon = declaration.IndexOf(':');
            if (colon < 0) throw new FormatException($"'{declaration}' is not a 'name: value' declaration.");

            var name = declaration[..colon].Trim();
            var value = declaration[(colon + 1)..].Trim();
            if (name.Length == 0) throw new FormatException($"'{declaration}': a declaration has no name.");

            var important = value.IndexOf("!important", StringComparison.OrdinalIgnoreCase);
            if (important >= 0) value = value[..important].Trim();
            result.Add((name, value));
        }
        return result;
    }

    /// <summary>Splits on the semicolons outside parentheses and quotes.</summary>
    private static IEnumerable<string> SplitDeclarations(string css)
    {
        var depth = 0;
        var quote = '\0';
        var start = 0;
        for (var i = 0; i < css.Length; i++)
        {
            var c = css[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (c == ';' && depth == 0)
            {
                var part = css[start..i].Trim();
                if (part.Length > 0) yield return part;
                start = i + 1;
            }
        }
        var last = css[start..].Trim();
        if (last.Length > 0) yield return last;
    }

    /// <summary>
    /// Replaces each <c>var(--name[, fallback])</c>. Returns null when a
    /// variable is undefined without a fallback, which drops the declaration
    /// as CSS does.
    /// </summary>
    public static string? Substitute(string value, Func<string, string?> lookup)
    {
        var at = value.IndexOf("var(", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return value;

        var result = new StringBuilder();
        var copied = 0;
        while (at >= 0)
        {
            result.Append(value, copied, at - copied);
            var close = MatchingParen(value, at + "var".Length);
            if (close < 0) throw new FormatException($"'{value}': var( is not closed.");

            var arguments = value[(at + "var(".Length)..close];
            if (Variable(arguments, lookup) is not { } replacement) return null;
            result.Append(replacement);

            copied = close + 1;
            at = value.IndexOf("var(", copied, StringComparison.OrdinalIgnoreCase);
        }
        result.Append(value, copied, value.Length - copied);
        return result.ToString();
    }

    /// <summary>The value of <c>--name</c> or of its fallback, from the arguments of a <c>var()</c>.</summary>
    private static string? Variable(string arguments, Func<string, string?> lookup)
    {
        var comma = arguments.IndexOf(',');
        var name = (comma < 0 ? arguments : arguments[..comma]).Trim();
        if (lookup(name) is { } value) return value;
        if (comma < 0) return null;
        return Substitute(arguments[(comma + 1)..].Trim(), lookup);
    }

    private static int MatchingParen(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static string Text(object? value) => value switch
    {
        null => "",
        string s => s.Trim(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>The first space-separated token, keeping a function call together.</summary>
    private static string First(object? value)
    {
        if (value is not string) return Text(value);
        var tokens = SplitTokens(Text(value));
        return tokens.Count > 0 ? tokens[0] : "";
    }

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

    /// <summary>A number, or a percentage as a fraction of one.</summary>
    private static double Amount(string text, string name) =>
        text.EndsWith('%') ? Number(text[..^1], name) / 100 : Number(text, name);

    /// <summary>A length in cells, bare or with a cell-sized unit. Pixels are refused.</summary>
    private static int Cells(object? value, string name)
    {
        switch (value)
        {
            case int i: return i;
            case long l: return checked((int)l);
            case double d: return (int)Math.Round(d);
        }

        var text = Text(value).ToLowerInvariant();
        if (text.EndsWith("px", StringComparison.Ordinal))
        {
            throw new FormatException($"'{text}' for '{name}': a terminal has no pixels; give the size in cells.");
        }
        if (double.TryParse(WithoutCellUnit(text), NumberStyles.Float, CultureInfo.InvariantCulture, out var cells))
        {
            return (int)Math.Round(cells);
        }
        throw Bad(name, value);
    }

    private static string WithoutCellUnit(string text)
    {
        var unit = CellUnits.FirstOrDefault(u => text.EndsWith(u, StringComparison.Ordinal));
        return unit is null ? text : text[..^unit.Length];
    }

    private static int? NullableCells(object? value, string name)
    {
        if (value is null) return null;
        var text = Text(value);
        if (text.Length == 0 || IsAutoKeyword(text)) return null;
        return Cells(value, name);
    }

    /// <summary>One length for both ends, or the start and then the end.</summary>
    private static (int Start, int End) Pair(object? value, string name)
    {
        if (value is int both) return (both, both);
        var parts = Text(value).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            1 => (Cells(parts[0], name), Cells(parts[0], name)),
            2 => (Cells(parts[0], name), Cells(parts[1], name)),
            _ => throw Bad(name, value),
        };
    }

    private static Edges WithInline(Edges edges, (int Start, int End) pair) => edges with { Left = pair.Start, Right = pair.End };

    private static Edges WithBlock(Edges edges, (int Start, int End) pair) => edges with { Top = pair.Start, Bottom = pair.End };

    private static Style Gap(Style style, object? value)
    {
        var (row, column) = Pair(value, "gap");
        return style with { RowGap = row, ColumnGap = column };
    }

    private static Length Len(object? value, string name)
    {
        switch (value)
        {
            case Length l: return l;
            case int i: return Length.Cells(i);
            case long l: return Length.Cells(checked((int)l));
            case double d: return Length.Cells((int)Math.Round(d));
        }

        var text = Text(value).ToLowerInvariant();
        if (text.Length == 0 || text is "auto" or "none" or "initial") return Length.Auto;
        if (text is "fit-content" or "min-content" or "max-content") return Length.FitContent;
        if (text.EndsWith('%') && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            return Length.Percent((int)Math.Round(percent));
        }
        return Length.Cells(Cells(text, name));
    }

    /// <summary>One value for all sides, two for vertical and horizontal, three, or four clockwise from the top.</summary>
    private static Edges EdgesOf(object? value, string name)
    {
        switch (value)
        {
            case Edges e: return e;
            case int i: return new Edges(i);
        }

        var parts = Text(value).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var numbers = parts.Select(part => Cells(part, name)).ToArray();
        return numbers.Length switch
        {
            1 => new Edges(numbers[0]),
            2 => new Edges(numbers[0], numbers[1]),
            3 => new Edges(numbers[0], numbers[1], numbers[2], numbers[1]),
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

    private static Display DisplayOf(object? value)
    {
        if (value is Display d) return d;
        return Text(value).ToLowerInvariant() switch
        {
            "block" or "list-item" or "inline-block" or "flow-root" or "table" or "table-row" or "table-cell" or "contents" => Display.Block,
            "inline" => Display.Inline,
            "flex" or "inline-flex" => Display.Flex,
            "grid" or "inline-grid" => Display.Grid,
            "none" => Display.None,
            _ => throw Bad("display", value),
        };
    }

    private static Visibility VisibilityOf(object? value)
    {
        if (value is Visibility v) return v;
        return Text(value).ToLowerInvariant() switch
        {
            "visible" => Visibility.Visible,
            "hidden" or "collapse" => Visibility.Hidden,
            _ => throw Bad("visibility", value),
        };
    }

    private static Overflow OverflowOf(object? value, string name)
    {
        if (value is Overflow o) return o;
        return Text(value).ToLowerInvariant() switch
        {
            "visible" => Overflow.Visible,
            "hidden" or "clip" => Overflow.Hidden,
            "scroll" or "auto" => Overflow.Scroll,
            _ => throw Bad(name, value),
        };
    }

    private static OverflowAnchor OverflowAnchorOf(object? value)
    {
        if (value is OverflowAnchor a) return a;
        return Text(value).ToLowerInvariant() switch
        {
            "auto" or "inherit" or "unset" or "initial" => OverflowAnchor.Auto,
            "none" => OverflowAnchor.None,
            _ => throw Bad("overflow-anchor", value),
        };
    }

    private static JustifyContent JustifyOf(object? value)
    {
        if (value is JustifyContent j) return j;
        return Text(value).ToLowerInvariant() switch
        {
            "flex-start" or "start" or "left" or "normal" => JustifyContent.FlexStart,
            "flex-end" or "end" or "right" => JustifyContent.FlexEnd,
            "center" => JustifyContent.Center,
            "space-between" => JustifyContent.SpaceBetween,
            "space-around" => JustifyContent.SpaceAround,
            "space-evenly" => JustifyContent.SpaceEvenly,
            _ => throw Bad("justify-content", value),
        };
    }

    private static AlignItems AlignItemsOf(object? value, string name)
    {
        if (value is AlignItems a) return a;
        return Text(value).ToLowerInvariant() switch
        {
            "stretch" or "normal" => AlignItems.Stretch,
            "flex-start" or "start" or "self-start" or "left" => AlignItems.FlexStart,
            "flex-end" or "end" or "self-end" or "right" => AlignItems.FlexEnd,
            "center" => AlignItems.Center,
            _ => throw Bad(name, value),
        };
    }

    private static AlignSelf AlignSelfOf(object? value)
    {
        if (value is AlignSelf a) return a;
        return Text(value).ToLowerInvariant() switch
        {
            "auto" => AlignSelf.Auto,
            "stretch" or "normal" => AlignSelf.Stretch,
            "flex-start" or "start" or "self-start" => AlignSelf.FlexStart,
            "flex-end" or "end" or "self-end" => AlignSelf.FlexEnd,
            "center" => AlignSelf.Center,
            _ => throw Bad("align-self", value),
        };
    }

    private static AlignContent AlignContentOf(object? value)
    {
        if (value is AlignContent a) return a;
        return Text(value).ToLowerInvariant() switch
        {
            "stretch" or "normal" => AlignContent.Stretch,
            "flex-start" or "start" => AlignContent.FlexStart,
            "flex-end" or "end" => AlignContent.FlexEnd,
            "center" => AlignContent.Center,
            "space-between" => AlignContent.SpaceBetween,
            "space-around" or "space-evenly" => AlignContent.SpaceAround,
            _ => throw Bad("align-content", value),
        };
    }

    private static Style FlexFlow(Style style, object? value)
    {
        foreach (var token in Text(value).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = token.Replace("-", "");
            if (System.Enum.TryParse<FlexDirection>(name, true, out var direction))
            {
                style = style with { FlexDirection = direction };
            }
            else if (System.Enum.TryParse<FlexWrap>(name, true, out var wrap))
            {
                style = style with { FlexWrap = wrap };
            }
            else
            {
                throw Bad("flex-flow", value);
            }
        }
        return style;
    }

    private static BorderStyle BorderStyleOf(object? value, string name)
    {
        if (value is BorderStyle b) return b;
        return ParseBorderStyle(Text(value).ToLowerInvariant()) ?? throw Bad(name, value);
    }

    private static BorderStyle? ParseBorderStyle(string token) => token switch
    {
        "none" or "hidden" => BorderStyle.None,
        "solid" or "groove" or "ridge" or "inset" or "outset" => BorderStyle.Solid,
        "double" => BorderStyle.Double,
        "dashed" => BorderStyle.Dashed,
        "dotted" => BorderStyle.Dotted,
        _ => null,
    };

    private static int BorderWidthOf(object? value, string name)
    {
        if (value is int i) return Math.Clamp(i, 0, 2);
        return First(value).ToLowerInvariant() switch
        {
            "thin" or "medium" => 1,
            "thick" => 2,
            var text => Math.Clamp(Cells(text, name), 0, 2),
        };
    }

    /// <summary>
    /// The <c>border</c> shorthand for every side or one: a width, a style
    /// and a colour in any order. Without a style the border is solid, since
    /// a border that was asked for should show.
    /// </summary>
    private static Style Border(Style style, object? value, Side? side)
    {
        if (value is BorderStyle typed) return WithSideStyle(style, typed, side);

        BorderStyle? lineStyle = null;
        foreach (var token in SplitTokens(Text(value).ToLowerInvariant()))
        {
            if (ParseBorderStyle(token) is { } parsed)
            {
                lineStyle = parsed;
            }
            else if (token is "thin" or "medium" or "thick" || char.IsDigit(token[0]))
            {
                var width = BorderWidthOf(token, "border");
                style = style with { BorderWidth = width };
                if (width == 0) lineStyle = BorderStyle.None;
            }
            else if (Color.TryParse(token, out var color))
            {
                style = style with { BorderColor = color };
            }
            else
            {
                var property = side is null ? "border" : "border-" + side.Value.ToString().ToLowerInvariant();
                throw Bad(property, value);
            }
        }
        return WithSideStyle(style, lineStyle ?? BorderStyle.Solid, side);
    }

    private static Style WithSideStyle(Style style, BorderStyle lineStyle, Side? side) => side switch
    {
        null => style with
        {
            BorderStyle = lineStyle,
            BorderTopStyle = null,
            BorderRightStyle = null,
            BorderBottomStyle = null,
            BorderLeftStyle = null,
        },
        Side.Top => style with { BorderTopStyle = lineStyle },
        Side.Right => style with { BorderRightStyle = lineStyle },
        Side.Bottom => style with { BorderBottomStyle = lineStyle },
        _ => style with { BorderLeftStyle = lineStyle },
    };

    /// <summary>Space-separated tokens, keeping <c>rgb(1, 2, 3)</c> together.</summary>
    private static List<string> SplitTokens(string text)
    {
        var tokens = new List<string>();
        var depth = 0;
        var start = -1;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;

            var separates = char.IsWhiteSpace(c) && depth == 0;
            if (separates && start >= 0)
            {
                tokens.Add(text[start..i]);
                start = -1;
            }
            else if (!separates && start < 0)
            {
                start = i;
            }
        }
        if (start >= 0) tokens.Add(text[start..]);
        return tokens;
    }

    private static Style TextColor(Style style, object? value)
    {
        if (value is Color typed) return style with { Color = typed, Set = style.Set | StyleSet.Color };

        var inherits = Text(value).ToLowerInvariant() is "inherit" or "currentcolor" or "unset" or "initial";
        if (inherits) return style with { Color = Color.Default, Set = style.Set & ~StyleSet.Color };
        return style with { Color = ColorOf(value, "color"), Set = style.Set | StyleSet.Color };
    }

    private static Style FontWeight(Style style, object? value)
    {
        var text = Text(value).ToLowerInvariant();
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight))
        {
            if (weight >= 600) return Bold(style);
            if (weight < 400) return Light(style);
            return NormalWeight(style);
        }
        return text switch
        {
            "bold" or "bolder" => Bold(style),
            "normal" or "inherit" or "unset" or "initial" => NormalWeight(style),
            "lighter" => Light(style),
            _ => throw Bad("font-weight", value),
        };
    }

    private static Style Bold(Style style) => SetFlag(ClearFlag(style, TextStyle.Dim), TextStyle.Bold);

    private static Style Light(Style style) => SetFlag(ClearFlag(style, TextStyle.Bold), TextStyle.Dim);

    private static Style NormalWeight(Style style) => ClearFlag(ClearFlag(style, TextStyle.Bold), TextStyle.Dim);

    private static Style FontStyle(Style style, object? value) => Text(value).ToLowerInvariant() switch
    {
        "italic" or "oblique" => SetFlag(style, TextStyle.Italic),
        "normal" or "inherit" or "unset" or "initial" => ClearFlag(style, TextStyle.Italic),
        _ => throw Bad("font-style", value),
    };

    /// <summary>
    /// <c>underline</c> and <c>line-through</c> set their flags and
    /// <c>none</c> clears both. Colours, line styles and thicknesses are
    /// accepted but not drawn.
    /// </summary>
    private static Style TextDecoration(Style style, object? value)
    {
        var tokens = SplitTokens(Text(value).ToLowerInvariant());
        if (tokens.Count == 0) throw Bad("text-decoration", value);
        foreach (var token in tokens)
        {
            style = token switch
            {
                "underline" => SetFlag(style, TextStyle.Underline),
                "line-through" => SetFlag(style, TextStyle.Strikethrough),
                "none" => ClearFlag(ClearFlag(style, TextStyle.Underline), TextStyle.Strikethrough),
                _ when IsUndrawnDecoration(token) => style,
                _ => throw Bad("text-decoration", value),
            };
        }
        return style;
    }

    private static bool IsUndrawnDecoration(string token) =>
        token is "overline" or "blink" or "solid" or "double" or "dotted" or "dashed" or "wavy"
            or "inherit" or "unset" or "initial" or "auto" or "from-font"
        || Color.TryParse(token, out _)
        || char.IsDigit(token[0]);

    /// <summary>Anything less than fully opaque is drawn dim.</summary>
    private static Style Opacity(Style style, object? value)
    {
        var amount = Amount(Text(value).ToLowerInvariant(), "opacity");
        return amount < 1 ? SetFlag(style, TextStyle.Dim) : ClearFlag(style, TextStyle.Dim);
    }

    /// <summary><c>invert()</c> of half or more swaps the colours; other filters are accepted and not drawn.</summary>
    private static Style Filter(Style style, object? value)
    {
        var text = Text(value).ToLowerInvariant();
        if (text is "none" or "inherit" or "unset" or "initial") return ClearFlag(style, TextStyle.Inverse);

        var at = text.IndexOf("invert(", StringComparison.Ordinal);
        if (at < 0) return style;
        var close = text.IndexOf(')', at);
        if (close < 0) throw Bad("filter", value);

        var argument = text[(at + "invert(".Length)..close].Trim();
        var amount = argument.Length == 0 ? 1 : Amount(argument, "filter");
        return amount >= 0.5 ? SetFlag(style, TextStyle.Inverse) : ClearFlag(style, TextStyle.Inverse);
    }

    private static UserSelect UserSelectOf(object? value)
    {
        if (value is UserSelect u) return u;
        return Text(value).ToLowerInvariant() switch
        {
            "auto" or "inherit" or "unset" or "initial" => UserSelect.Auto,
            "none" => UserSelect.None,
            "text" => UserSelect.Text,
            "all" => UserSelect.All,
            "contain" => UserSelect.Contain,
            _ => throw Bad("user-select", value),
        };
    }

    private static ContainerType ContainerTypeOf(object? value)
    {
        if (value is ContainerType t) return t;
        return Text(value).ToLowerInvariant() switch
        {
            "normal" => ContainerType.Normal,
            "size" => ContainerType.Size,
            "inline-size" => ContainerType.InlineSize,
            _ => throw Bad("container-type", value),
        };
    }

    private static IReadOnlyList<string> ContainerNamesOf(object? value)
    {
        if (value is IReadOnlyList<string> list) return list;
        var text = Text(value);
        if (text.Length == 0 || text.Equals("none", StringComparison.OrdinalIgnoreCase)) return [];

        var names = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (names.Any(name => !char.IsLetter(name[0]) && name[0] != '_')) throw Bad("container-name", value);
        return names;
    }

    /// <summary>The <c>container</c> shorthand: <c>names [/ type]</c>.</summary>
    private static Style Container(Style style, object? value)
    {
        var text = Text(value);
        var slash = text.IndexOf('/');
        var names = slash < 0 ? text : text[..slash];
        var type = slash < 0 ? "normal" : text[(slash + 1)..].Trim();
        return style with { ContainerNames = ContainerNamesOf(names.Trim()), ContainerType = ContainerTypeOf(type) };
    }

    private static WhiteSpace WhiteSpaceOf(object? value)
    {
        if (value is WhiteSpace w) return w;
        return Text(value).ToLowerInvariant() switch
        {
            "normal" => WhiteSpace.Normal,
            "nowrap" => WhiteSpace.NoWrap,
            "pre" => WhiteSpace.Pre,
            "pre-wrap" or "break-spaces" => WhiteSpace.PreWrap,
            "pre-line" => WhiteSpace.PreLine,
            _ => throw Bad("white-space", value),
        };
    }

    /// <summary>One value is for the end of the line; two are for the start and the end.</summary>
    private static TextOverflow TextOverflowOf(object? value)
    {
        if (value is TextOverflow typed) return typed;
        var parts = Text(value).ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (parts.Length)
        {
            case 1:
                return IsEllipsis(parts[0]) ? TextOverflow.Ellipsis : TextOverflow.Clip;
            case 2:
                var start = IsEllipsis(parts[0]);
                var end = IsEllipsis(parts[1]);
                if (start && end) return TextOverflow.EllipsisMiddle;
                if (start) return TextOverflow.EllipsisStart;
                return end ? TextOverflow.Ellipsis : TextOverflow.Clip;
            default:
                throw Bad("text-overflow", value);
        }
    }

    private static bool IsEllipsis(string token) => token switch
    {
        "clip" => false,
        "ellipsis" => true,
        _ => throw new FormatException($"'{token}' is not a valid value for 'text-overflow'."),
    };

    private static TextAlign TextAlignOf(object? value)
    {
        if (value is TextAlign t) return t;
        return Text(value).ToLowerInvariant() switch
        {
            "left" or "start" or "justify" => TextAlign.Left,
            "center" => TextAlign.Center,
            "right" or "end" => TextAlign.Right,
            _ => throw Bad("text-align", value),
        };
    }

    private static Style SetFlag(Style style, TextStyle flag) =>
        style with { TextStyle = style.TextStyle | flag, TextStyleReset = style.TextStyleReset & ~flag };

    /// <summary>Clears a flag and records that it was cleared, so inheritance does not set it again.</summary>
    private static Style ClearFlag(Style style, TextStyle flag) =>
        style with { TextStyle = style.TextStyle & ~flag, TextStyleReset = style.TextStyleReset | flag };

    /// <summary>The <c>flex</c> shorthand: <c>grow [shrink] [basis]</c>, <c>none</c>, <c>auto</c> or <c>initial</c>.</summary>
    private static Style Flex(Style style, object? value)
    {
        if (value is int or double or long or float)
        {
            return style with { FlexGrow = Number(value, "flex"), FlexShrink = 1, FlexBasis = Length.Cells(0) };
        }

        var text = Text(value).ToLowerInvariant();
        switch (text)
        {
            case "none": return style with { FlexGrow = 0, FlexShrink = 0, FlexBasis = Length.Auto };
            case "auto": return style with { FlexGrow = 1, FlexShrink = 1, FlexBasis = Length.Auto };
            case "initial": return style with { FlexGrow = 0, FlexShrink = 1, FlexBasis = Length.Auto };
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 3) throw Bad("flex", value);
        var grow = Number(parts[0], "flex");
        var shrink = parts.Length > 1 ? Number(parts[1], "flex") : 1;
        var basis = parts.Length > 2 ? Len(parts[2], "flex") : Length.Cells(0);
        return style with { FlexGrow = grow, FlexShrink = shrink, FlexBasis = basis };
    }

    /// <summary>
    /// A track template such as <c>20 25% 1fr auto</c>, with <c>repeat(n, …)</c> expanded.
    /// </summary>
    private static TrackList Tracks(object? value, string name)
    {
        switch (value)
        {
            case TrackList list: return list;
            case IReadOnlyList<Track> tracks: return new TrackList(tracks);
            case Track track: return [track];
            case int cells: return [Track.Cells(cells)];
        }

        var text = Text(value);
        var result = new TrackList();
        var position = 0;
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                position++;
            }
            else if (text.AsSpan(position).StartsWith("repeat(", StringComparison.OrdinalIgnoreCase))
            {
                position = ReadRepeat(text, position, result, name, value);
            }
            else
            {
                var end = position;
                while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
                result.Add(TrackOf(text[position..end], name));
                position = end;
            }
        }
        return result;
    }

    /// <summary>Expands <c>repeat(n, tracks)</c> into the list and returns the position after it.</summary>
    private static int ReadRepeat(string text, int start, TrackList into, string name, object? value)
    {
        var close = text.IndexOf(')', start);
        if (close < 0) throw Bad(name, value);

        var arguments = text[(start + "repeat(".Length)..close];
        var comma = arguments.IndexOf(',');
        if (comma < 0 || !int.TryParse(arguments[..comma].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
        {
            throw Bad(name, value);
        }

        var tracks = Tracks(arguments[(comma + 1)..], name);
        for (var n = 0; n < count; n++) into.AddRange(tracks);
        return close + 1;
    }

    private static Track TrackOf(string token, string name)
    {
        if (IsAutoKeyword(token)) return Track.Auto;
        if (token.EndsWith("fr", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(token[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var fraction))
        {
            return Track.Fr(fraction);
        }
        if (token.EndsWith('%') && double.TryParse(token[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            return Track.Percent(percent);
        }
        return Track.Cells(Cells(token, name));
    }

    private static Style GridColumn(Style style, object? value)
    {
        var (start, span) = Placement(value, "grid-column");
        return style with { GridColumnStart = start, GridColumnSpan = span };
    }

    private static Style GridRow(Style style, object? value)
    {
        var (start, span) = Placement(value, "grid-row");
        return style with { GridRowStart = start, GridRowSpan = span };
    }

    /// <summary>
    /// A grid placement: <c>2</c>, <c>2 / 4</c>, <c>2 / span 2</c>, <c>span 2</c> or <c>auto</c>.
    /// </summary>
    private static (int? Start, int Span) Placement(object? value, string name)
    {
        if (value is int line) return (line, 1);
        var text = Text(value);
        if (text.Length == 0 || IsAutoKeyword(text)) return (null, 1);

        var parts = text.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length > 2) throw Bad(name, value);

        int? start = null;
        var span = 1;
        if (TryParseSpan(parts[0], name, out var startSpan))
        {
            span = startSpan;
        }
        else if (!IsAutoKeyword(parts[0]))
        {
            start = Cells(parts[0], name);
        }

        if (parts.Length == 2)
        {
            var end = parts[1];
            if (TryParseSpan(end, name, out var endSpan))
            {
                span = endSpan;
            }
            else if (!IsAutoKeyword(end))
            {
                if (start is not { } first) throw Bad(name, value);
                span = Math.Max(1, Cells(end, name) - first);
            }
        }
        return (start, span);
    }

    /// <summary>The span implied by an end line and the start line already set.</summary>
    private static int SpanFromEnd(object? value, int? start, string name)
    {
        var text = Text(value);
        if (TryParseSpan(text, name, out var span)) return span;
        if (text.Length == 0 || IsAutoKeyword(text)) return 1;

        var line = Cells(value, name);
        if (start is not { } first) throw new FormatException($"'{name}' needs a start line before an end line.");
        return Math.Max(1, line - first);
    }

    private static bool TryParseSpan(string text, string name, out int span)
    {
        span = 1;
        if (!text.StartsWith("span ", StringComparison.OrdinalIgnoreCase)) return false;
        span = Math.Max(1, Cells(text["span ".Length..], name));
        return true;
    }

    private static bool IsAutoKeyword(string text) => text.Equals("auto", StringComparison.OrdinalIgnoreCase);

    private static FormatException Bad(string name, object? value) =>
        new($"'{value}' is not a valid value for '{name}'.");
}
