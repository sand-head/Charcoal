using System.Text;
using SlopTui.Components;

namespace SlopTui.Styling;

/// <summary>How a compound selector relates to the one on its left.</summary>
public enum Combinator
{
    /// <summary>The first compound of a selector.</summary>
    None,
    /// <summary>A space: any descendant element.</summary>
    Descendant,
    /// <summary><c>&gt;</c>: a child element, ignoring the components in between.</summary>
    Child,
}

/// <summary>
/// A type, classes, an id and focus pseudo classes that must all hold for
/// one element.
/// </summary>
public sealed record CompoundSelector(
    string? Type,
    IReadOnlyList<string> Classes,
    string? Id,
    bool Focus,
    bool FocusWithin,
    Combinator Combinator)
{
    public bool Matches(HostElement element, HostElement? focused)
    {
        if (Type is not null && Type != "*" && !string.Equals(Type, element.Name, StringComparison.Ordinal)) return false;
        if (Id is not null && !string.Equals(Id, element.Id, StringComparison.Ordinal)) return false;
        foreach (var name in Classes)
        {
            if (!element.Classes.Contains(name)) return false;
        }
        if (Focus && !ReferenceEquals(element, focused)) return false;
        if (FocusWithin && !Contains(element, focused)) return false;
        return true;
    }

    private static bool Contains(HostElement element, HostElement? focused)
    {
        for (HostNode? node = focused; node is not null; node = node.Parent)
        {
            if (ReferenceEquals(node, element)) return true;
        }
        return false;
    }

    public override string ToString()
    {
        var text = new StringBuilder(Type ?? "");
        if (Id is not null) text.Append('#').Append(Id);
        foreach (var name in Classes) text.Append('.').Append(name);
        if (Focus) text.Append(":focus");
        if (FocusWithin) text.Append(":focus-within");
        return text.Length == 0 ? "*" : text.ToString();
    }
}

/// <summary>Compound selectors joined by combinators, matched from the right.</summary>
public sealed class Selector
{
    private Selector(IReadOnlyList<CompoundSelector> compounds, string text)
    {
        Compounds = compounds;
        Text = text;
        Specificity = SpecificityOf(compounds);
    }

    /// <summary>The compounds, left to right.</summary>
    public IReadOnlyList<CompoundSelector> Compounds { get; }

    public string Text { get; }

    /// <summary>CSS specificity as ids × 10000 + (classes and pseudo classes) × 100 + types.</summary>
    public int Specificity { get; }

    /// <summary>Whether a focus pseudo class appears left of a combinator, so focus can restyle descendants.</summary>
    public bool FocusAffectsDescendants =>
        Compounds.Count > 1 && Compounds.Take(Compounds.Count - 1).Any(c => c.Focus || c.FocusWithin);

    public bool DependsOnFocus => Compounds.Any(c => c.Focus || c.FocusWithin);

    public bool Matches(HostElement element, HostElement? focused) => MatchFrom(Compounds.Count - 1, element, focused);

    private static int SpecificityOf(IReadOnlyList<CompoundSelector> compounds)
    {
        var ids = 0;
        var classes = 0;
        var types = 0;
        foreach (var compound in compounds)
        {
            if (compound.Id is not null) ids++;
            classes += compound.Classes.Count;
            if (compound.Focus) classes++;
            if (compound.FocusWithin) classes++;
            if (compound.Type is not null && compound.Type != "*") types++;
        }
        return ids * 10_000 + classes * 100 + types;
    }

    private bool MatchFrom(int index, HostElement element, HostElement? focused)
    {
        var compound = Compounds[index];
        if (!compound.Matches(element, focused)) return false;
        if (index == 0) return true;

        switch (compound.Combinator)
        {
            case Combinator.Child:
                var parent = ParentElement(element);
                return parent is not null && MatchFrom(index - 1, parent, focused);
            case Combinator.Descendant:
                for (var ancestor = ParentElement(element); ancestor is not null; ancestor = ParentElement(ancestor))
                {
                    if (MatchFrom(index - 1, ancestor, focused)) return true;
                }
                return false;
            default:
                return false;
        }
    }

    private static HostElement? ParentElement(HostElement element) => element.Parent?.ClosestElement;

    /// <summary>Parses one selector without commas.</summary>
    /// <exception cref="FormatException">The selector is malformed or uses something unsupported.</exception>
    public static Selector Parse(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) throw new FormatException("An empty selector.");
        return new Selector(new SelectorReader(trimmed).ReadCompounds(), trimmed);
    }

    public override string ToString() => Text;

    private sealed class SelectorReader(string text)
    {
        private int _position;

        private bool AtEnd => _position >= text.Length;
        private char Current => text[_position];

        public List<CompoundSelector> ReadCompounds()
        {
            var compounds = new List<CompoundSelector>();
            while (!AtEnd)
            {
                var combinator = ReadCombinator();
                if (AtEnd)
                {
                    if (combinator != Combinator.None) throw Error("a selector cannot end with a combinator");
                    break;
                }

                var isFirst = compounds.Count == 0;
                if (isFirst && combinator != Combinator.None) throw Error("a selector cannot start with a combinator");
                if (!isFirst && combinator == Combinator.None) throw Error("compounds must be separated by a space or '>'");
                compounds.Add(ReadCompound(combinator));
            }
            if (compounds.Count == 0) throw new FormatException($"'{text}' selects nothing.");
            return compounds;
        }

        private Combinator ReadCombinator()
        {
            var combinator = Combinator.None;
            while (!AtEnd && (char.IsWhiteSpace(Current) || Current == '>'))
            {
                if (Current == '>')
                {
                    combinator = Combinator.Child;
                }
                else if (combinator == Combinator.None)
                {
                    combinator = Combinator.Descendant;
                }
                _position++;
            }
            return combinator;
        }

        private CompoundSelector ReadCompound(Combinator combinator)
        {
            string? type = null;
            string? id = null;
            var classes = new List<string>();
            var focus = false;
            var focusWithin = false;

            var start = _position;
            while (!AtEnd && !char.IsWhiteSpace(Current) && Current != '>')
            {
                var isFirstPart = _position == start;
                switch (Current)
                {
                    case '.':
                        _position++;
                        classes.Add(ReadName("class"));
                        break;
                    case '#':
                        _position++;
                        id = ReadName("id");
                        break;
                    case ':':
                        _position++;
                        var pseudoClass = ReadName("pseudo-class");
                        if (pseudoClass == "focus")
                        {
                            focus = true;
                        }
                        else if (pseudoClass == "focus-within")
                        {
                            focusWithin = true;
                        }
                        else
                        {
                            throw Error($"':{pseudoClass}' is not a supported pseudo-class (focus, focus-within)");
                        }
                        break;
                    case '*':
                        if (!isFirstPart) throw Error("a type must come first in a compound");
                        type = "*";
                        _position++;
                        break;
                    case var c when IsNameChar(c):
                        if (!isFirstPart) throw Error("a type must come first in a compound");
                        type = ReadName("type");
                        break;
                    default:
                        throw Error($"unexpected '{Current}'");
                }
            }
            return new CompoundSelector(type, classes, id, focus, focusWithin, combinator);
        }

        private string ReadName(string kind)
        {
            var start = _position;
            while (!AtEnd && IsNameChar(Current)) _position++;
            if (_position == start) throw Error($"a {kind} name is missing at position {start}");
            return text[start.._position];
        }

        private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '-' or '_';

        private FormatException Error(string problem) => new($"'{text}': {problem}.");
    }
}
