using System.Text;
using SlopTui.Layout;

namespace SlopTui.Styling;

/// <summary>A <c>name: value</c> declaration. <see cref="Known"/> is false for an unsupported property.</summary>
public sealed record Declaration(string Name, string Value, bool Known)
{
    /// <summary>Whether this declares a custom property, which <c>var()</c> reads.</summary>
    public bool IsCustom => StyleParser.IsCustomProperty(Name);
}

/// <summary>A rule and its position in the sheet.</summary>
public sealed record StyleRule(IReadOnlyList<Selector> Selectors, IReadOnlyList<Declaration> Declarations, int Order);

/// <summary>
/// A parsed stylesheet: selector lists, comments, custom properties and
/// declarations. At-rules are skipped with a warning, and <c>!important</c>
/// is accepted and ignored.
/// </summary>
/// <remarks>
/// A bad value fails the parse with its line, selector and property. A CSS
/// property a terminal cannot draw is accepted and ignored; an unknown one is
/// kept but ignored, with a warning.
/// </remarks>
public sealed class Stylesheet
{
    private Stylesheet(IReadOnlyList<StyleRule> rules, IReadOnlyList<string> warnings, string source)
    {
        Rules = rules;
        Warnings = warnings;
        Source = source;
    }

    /// <summary>The rules in source order.</summary>
    public IReadOnlyList<StyleRule> Rules { get; }

    /// <summary>One line for each thing the parser skipped.</summary>
    public IReadOnlyList<string> Warnings { get; }

    public string Source { get; }

    public bool DependsOnFocus => Rules.Any(r => r.Selectors.Any(s => s.DependsOnFocus));

    /// <summary>Whether a focus pseudo class appears left of a combinator, so focus can restyle descendants.</summary>
    public bool FocusAffectsDescendants => Rules.Any(r => r.Selectors.Any(s => s.FocusAffectsDescendants));

    /// <exception cref="FormatException">The CSS is malformed; the message names the line.</exception>
    public static Stylesheet Parse(string css)
    {
        ArgumentNullException.ThrowIfNull(css);
        var text = StripComments(css);
        var rules = new List<StyleRule>();
        var warnings = new List<string>();
        var position = 0;
        while (true)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
            if (position >= text.Length) break;

            if (text[position] == '@')
            {
                position = SkipAtRule(text, position, warnings);
                continue;
            }
            rules.Add(ParseRule(text, ref position, rules.Count, warnings));
        }
        return new Stylesheet(rules, warnings, css);
    }

    private static StyleRule ParseRule(string text, ref int position, int order, List<string> warnings)
    {
        var start = position;
        var line = LineOf(text, start);
        var open = text.IndexOf('{', start);
        if (open < 0) throw new FormatException($"line {line}: expected '{{' after '{text[start..].Trim()}'.");

        var selectorText = text[start..open].Trim();
        var close = text.IndexOf('}', open);
        if (close < 0) throw new FormatException($"line {LineOf(text, open)}: '{selectorText}' has no closing '}}'.");
        if (selectorText.Contains('}')) throw new FormatException($"line {line}: a rule body was not opened before '{selectorText}'.");

        var selectors = ParseSelectors(selectorText, line);
        var body = new RuleBody(text[(open + 1)..close], selectorText, LineOf(text, open));
        var declarations = ParseDeclarations(body, warnings);
        position = close + 1;
        return new StyleRule(selectors, declarations, order);
    }

    private static List<Selector> ParseSelectors(string selectorText, int line)
    {
        var selectors = new List<Selector>();
        foreach (var part in selectorText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                selectors.Add(Selector.Parse(part));
            }
            catch (FormatException ex)
            {
                throw new FormatException($"line {line}: {ex.Message}", ex);
            }
        }
        if (selectors.Count == 0) throw new FormatException($"line {line}: a rule with no selector.");
        return selectors;
    }

    private readonly record struct RuleBody(string Text, string Selector, int Line)
    {
        public FormatException Error(string problem, Exception? inner = null) =>
            new($"line {Line}: in '{Selector}', {problem}", inner);
    }

    private static List<Declaration> ParseDeclarations(RuleBody body, List<string> warnings)
    {
        List<(string Name, string Value)> parsed;
        try
        {
            parsed = StyleParser.ParseDeclarations(body.Text);
        }
        catch (FormatException ex)
        {
            throw body.Error(ex.Message, ex);
        }

        var declarations = new List<Declaration>();
        foreach (var (name, value) in parsed)
        {
            var isProperty = StyleParser.IsProperty(name);
            var known = isProperty || StyleParser.IsIgnored(name) || StyleParser.IsCustomProperty(name);
            // A value with var() can only be checked once the variables are known.
            if (isProperty && !value.Contains("var(", StringComparison.OrdinalIgnoreCase))
            {
                Validate(body, name, value);
            }
            else if (!known)
            {
                warnings.Add($"line {body.Line}: '{name}' in '{body.Selector}' is not a property this library knows; ignored.");
            }
            declarations.Add(new Declaration(name, value, known));
        }
        return declarations;
    }

    /// <summary>Skips a statement at-rule or a block at-rule and returns the position after it.</summary>
    private static int SkipAtRule(string text, int start, List<string> warnings)
    {
        var end = text.IndexOfAny([';', '{'], start);
        var name = text[start..(end < 0 ? text.Length : end)].Trim();
        warnings.Add($"line {LineOf(text, start)}: '{name}' is an at-rule; skipped.");
        if (end < 0) return text.Length;
        if (text[end] == ';') return end + 1;

        var close = MatchingBrace(text, end);
        if (close < 0) throw new FormatException($"line {LineOf(text, start)}: '{name}' is not closed.");
        return close + 1;
    }

    /// <summary>The index of the brace that closes the one at <paramref name="open"/>, or -1.</summary>
    private static int MatchingBrace(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static void Validate(RuleBody body, string name, string value)
    {
        try
        {
            StyleParser.Apply(Style.Default, name, value);
        }
        catch (FormatException ex)
        {
            throw body.Error($"'{name}: {value}' — {ex.Message}", ex);
        }
    }

    /// <summary>Blanks out comments, keeping line breaks so line numbers still hold.</summary>
    private static string StripComments(string css)
    {
        var result = new StringBuilder(css.Length);
        var i = 0;
        while (i < css.Length)
        {
            var isCommentStart = i + 1 < css.Length && css[i] == '/' && css[i + 1] == '*';
            if (!isCommentStart)
            {
                result.Append(css[i++]);
                continue;
            }

            var end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
            if (end < 0) throw new FormatException($"line {LineOf(css, i)}: a comment is not closed.");
            for (var j = i; j < end + 2; j++)
            {
                result.Append(css[j] == '\n' ? '\n' : ' ');
            }
            i = end + 2;
        }
        return result.ToString();
    }

    private static int LineOf(string text, int index) =>
        1 + text.AsSpan(0, Math.Min(index, text.Length)).Count('\n');
}
