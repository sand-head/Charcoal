using SlopTui.Components;
using SlopTui.Layout;
using SlopTui.Rendering;

namespace SlopTui.Styling;

/// <summary>
/// Resolves an element's style: matching rules by specificity, sheet and
/// order, then the element's own attributes, then inheritance.
/// </summary>
/// <remarks>
/// A <c>text</c> element without a colour takes its nearest ancestor's, and
/// text-style flags accumulate down the tree.
/// </remarks>
public static class StyleResolver
{
    public static Style Resolve(HostElement element, IReadOnlyList<Stylesheet> sheets, HostElement? focused)
    {
        var style = element.BaseStyle;
        foreach (var rule in MatchingRules(element, sheets, focused))
        {
            foreach (var declaration in rule.Declarations)
            {
                if (declaration.Known) style = StyleParser.Apply(style, declaration.Name, declaration.Value);
            }
        }
        style = element.ApplyOwnAttributes(style);
        return Inherit(element, style);
    }

    public static bool Matches(HostElement element, Selector selector, HostElement? focused = null) =>
        selector.Matches(element, focused);

    /// <summary>The rules that match, lowest precedence first.</summary>
    private static IEnumerable<StyleRule> MatchingRules(HostElement element, IReadOnlyList<Stylesheet> sheets, HostElement? focused)
    {
        var matched = new List<(int Specificity, int Sheet, int Order, StyleRule Rule)>();
        for (var sheet = 0; sheet < sheets.Count; sheet++)
        {
            foreach (var rule in sheets[sheet].Rules)
            {
                if (HighestMatchingSpecificity(rule, element, focused) is { } specificity)
                {
                    matched.Add((specificity, sheet, rule.Order, rule));
                }
            }
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

    /// <summary>Gives a text element its ancestors' colour and text-style flags.</summary>
    internal static Style Inherit(HostElement element, Style style)
    {
        if (!element.IsText) return style;
        var color = style.Color;
        var flags = style.TextStyle;
        for (var ancestor = element.Parent?.ClosestElement; ancestor is not null; ancestor = ancestor.Parent?.ClosestElement)
        {
            var ancestorStyle = ancestor.Node.Style;
            if (color.Kind == ColorKind.Default && ancestorStyle.Color.Kind != ColorKind.Default)
            {
                color = ancestorStyle.Color;
            }
            flags |= ancestorStyle.TextStyle;
        }

        var unchanged = color == style.Color && flags == style.TextStyle;
        return unchanged ? style : style with { Color = color, TextStyle = flags };
    }
}
