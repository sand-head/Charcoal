using SlopTui.Components;
using SlopTui.Layout;
using SlopTui.Rendering;

namespace SlopTui.Styling;

/// <summary>
/// Resolves an element's style: the user-agent sheet, then the app's sheets
/// by specificity, sheet and order, then its inline <c>style</c>, then what it
/// inherits from its parent.
/// </summary>
/// <remarks>
/// As in CSS, <c>color</c>, the text flags, <c>white-space</c>,
/// <c>text-align</c> and custom properties inherit unless the element sets
/// them. Every element inherits them, so a box carries the look of its text.
/// </remarks>
public static class StyleResolver
{
    public static Style Resolve(HostElement element, IReadOnlyList<Stylesheet> sheets, HostElement? focused, MediaEnvironment? media = null)
    {
        var parent = element.Parent?.ClosestElement?.Node.Style;
        var declarations = CascadedDeclarations(element, sheets, focused, media ?? MediaEnvironment.Default);
        var custom = CustomProperties(declarations, parent?.CustomProperties ?? Style.NoCustomProperties);

        var style = Style.Default with { CustomProperties = custom };
        foreach (var (name, value) in declarations)
        {
            if (StyleParser.IsCustomProperty(name)) continue;
            if (StyleParser.Substitute(value, key => custom.GetValueOrDefault(key)) is { } resolved)
            {
                style = StyleParser.Apply(style, name, resolved);
            }
        }
        return Inherit(style, parent);
    }

    public static bool Matches(HostElement element, Selector selector, HostElement? focused = null) =>
        selector.Matches(element, focused);

    /// <summary>The declarations of every matching rule in cascade order, then the inline style's.</summary>
    private static List<(string Name, string Value)> CascadedDeclarations(
        HostElement element, IReadOnlyList<Stylesheet> sheets, HostElement? focused, MediaEnvironment media)
    {
        var declarations = new List<(string Name, string Value)>();
        foreach (var rule in MatchingRules(element, sheets, focused, media))
        {
            foreach (var declaration in rule.Declarations)
            {
                if (declaration.Known) declarations.Add((declaration.Name, declaration.Value));
            }
        }
        if (element.InlineStyle is { } css) declarations.AddRange(StyleParser.ParseDeclarations(css));
        return declarations;
    }

    /// <summary>The inherited custom properties with the element's own declared over them.</summary>
    private static IReadOnlyDictionary<string, string> CustomProperties(
        List<(string Name, string Value)> declarations, IReadOnlyDictionary<string, string> inherited)
    {
        Dictionary<string, string>? own = null;
        foreach (var (name, value) in declarations)
        {
            if (!StyleParser.IsCustomProperty(name)) continue;
            own ??= new Dictionary<string, string>(inherited, StringComparer.Ordinal);
            own[name] = value;
        }
        return own ?? inherited;
    }

    /// <summary>The rules that match, lowest precedence first, starting with the user-agent sheet's.</summary>
    private static IEnumerable<StyleRule> MatchingRules(HostElement element, IReadOnlyList<Stylesheet> sheets, HostElement? focused, MediaEnvironment media)
    {
        var matched = new List<(int Specificity, int Sheet, int Order, StyleRule Rule)>();
        void Collect(Stylesheet sheet, int sheetIndex)
        {
            foreach (var rule in sheet.Rules)
            {
                if (rule.Media is { } query && !query.Matches(media)) continue;
                if (HighestMatchingSpecificity(rule, element, focused) is { } specificity)
                {
                    matched.Add((specificity, sheetIndex, rule.Order, rule));
                }
            }
        }

        Collect(UserAgentStylesheet.Sheet, -1);
        for (var sheet = 0; sheet < sheets.Count; sheet++)
        {
            Collect(sheets[sheet], sheet);
        }
        matched.Sort((a, b) => (a.Specificity, a.Sheet, a.Order).CompareTo((b.Specificity, b.Sheet, b.Order)));
        return matched.Select(m => m.Rule);
    }

    private static int? HighestMatchingSpecificity(StyleRule rule, HostElement element, HostElement? focused)
    {
        int? highest = null;
        foreach (var selector in rule.Selectors)
        {
            if (selector.Specificity > (highest ?? -1) && selector.Matches(element, focused))
            {
                highest = selector.Specificity;
            }
        }
        return highest;
    }

    /// <summary>Takes the inherited properties from the parent, except those the element set itself.</summary>
    public static Style Inherit(Style style, Style? parent)
    {
        if (parent is null) return style;
        var color = style.Set.HasFlag(StyleSet.Color) ? style.Color : parent.Color;
        var flags = (parent.TextStyle & ~style.TextStyleReset) | style.TextStyle;
        var whiteSpace = style.Set.HasFlag(StyleSet.WhiteSpace) ? style.WhiteSpace : parent.WhiteSpace;
        var align = style.Set.HasFlag(StyleSet.TextAlign) ? style.TextAlign : parent.TextAlign;

        var unchanged = color == style.Color && flags == style.TextStyle && whiteSpace == style.WhiteSpace && align == style.TextAlign;
        if (unchanged) return style;
        return style with { Color = color, TextStyle = flags, WhiteSpace = whiteSpace, TextAlign = align };
    }
}
