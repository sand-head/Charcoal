using System.Numerics;
using System.Text;
using Charcoal.Components;

namespace Charcoal.Styling;

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

/// <summary><c>[name]</c>, which requires the attribute, or <c>[name=value]</c>, which requires that value.</summary>
public sealed record AttributeSelector(string Name, string? Value)
{
    public bool Matches(HostElement element)
    {
        if (!element.Attributes.TryGetValue(Name, out var actual)) return false;
        if (actual is false) return false;
        if (Value is null) return true;

        var text = actual is true ? "" : actual?.ToString() ?? "";
        return string.Equals(text, Value, StringComparison.Ordinal);
    }

    public override string ToString() => Value is null ? $"[{Name}]" : $"[{Name}=\"{Value}\"]";
}

[Flags]
public enum PseudoClass
{
    None = 0,
    Focus = 1,
    FocusWithin = 2,
    Root = 4,
    FirstChild = 8,
    LastChild = 16,
    Disabled = 32,
    Enabled = 64,
    PlaceholderShown = 128,
}

/// <summary>
/// A type, classes, an id, attribute conditions and pseudo-classes that must
/// all hold for one element.
/// </summary>
public sealed record CompoundSelector(
    string? Type,
    IReadOnlyList<string> Classes,
    string? Id,
    IReadOnlyList<AttributeSelector> Attributes,
    PseudoClass Pseudo,
    Combinator Combinator)
{
    public bool Focus => Has(PseudoClass.Focus);
    public bool FocusWithin => Has(PseudoClass.FocusWithin);

    private bool Has(PseudoClass pseudoClass) => (Pseudo & pseudoClass) != 0;

    public bool Matches(HostElement element, HostElement? focused)
    {
        if (Type is not null && Type != "*" && !string.Equals(Type, element.Name, StringComparison.OrdinalIgnoreCase)) return false;
        if (Id is not null && !string.Equals(Id, element.Id, StringComparison.Ordinal)) return false;
        foreach (var name in Classes)
        {
            if (!element.Classes.Contains(name)) return false;
        }
        foreach (var attribute in Attributes)
        {
            if (!attribute.Matches(element)) return false;
        }
        return MatchesPseudoClasses(element, focused);
    }

    private bool MatchesPseudoClasses(HostElement element, HostElement? focused)
    {
        if (Focus && !ReferenceEquals(element, focused)) return false;
        if (FocusWithin && !Contains(element, focused)) return false;
        if (Has(PseudoClass.Root) && element.Parent?.ClosestElement is not null) return false;
        if (Has(PseudoClass.FirstChild) && !IsFirstChild(element)) return false;
        if (Has(PseudoClass.LastChild) && !IsLastChild(element)) return false;
        if (Has(PseudoClass.Disabled) && !IsDisabled(element)) return false;
        if (Has(PseudoClass.Enabled) && (!IsFormControl(element) || IsDisabled(element))) return false;
        if (Has(PseudoClass.PlaceholderShown) && element.Control is not { PlaceholderShown: true }) return false;
        return true;
    }

    /// <summary>The elements that <c>:enabled</c> and <c>:disabled</c> apply to.</summary>
    private static bool IsFormControl(HostElement element) =>
        element.Name is "input" or "textarea" or "button" or "select" or "option" or "optgroup" or "fieldset";

    private static bool IsDisabled(HostElement element) =>
        IsFormControl(element) && element.Attributes.GetValueOrDefault("disabled") is not (null or false);

    private static bool Contains(HostElement element, HostElement? focused)
    {
        for (HostNode? node = focused; node is not null; node = node.Parent)
        {
            if (ReferenceEquals(node, element)) return true;
        }
        return false;
    }

    private static bool IsFirstChild(HostElement element)
    {
        var parent = element.Parent?.ClosestElement;
        return parent is null || ReferenceEquals(ElementChildren(parent).FirstOrDefault(), element);
    }

    private static bool IsLastChild(HostElement element)
    {
        var parent = element.Parent?.ClosestElement;
        return parent is null || ReferenceEquals(ElementChildren(parent).LastOrDefault(), element);
    }

    /// <summary>An element's child elements, looking through the components in between.</summary>
    private static IEnumerable<HostElement> ElementChildren(HostNode node)
    {
        foreach (var child in node.Children)
        {
            if (child is HostElement element)
            {
                yield return element;
            }
            else if (child is HostContainer container)
            {
                foreach (var inner in ElementChildren(container)) yield return inner;
            }
        }
    }

    public override string ToString()
    {
        var text = new StringBuilder(Type ?? "");
        if (Id is not null) text.Append('#').Append(Id);
        foreach (var name in Classes) text.Append('.').Append(name);
        foreach (var attribute in Attributes) text.Append(attribute);
        if (Focus) text.Append(":focus");
        if (FocusWithin) text.Append(":focus-within");
        if (Has(PseudoClass.Root)) text.Append(":root");
        if (Has(PseudoClass.FirstChild)) text.Append(":first-child");
        if (Has(PseudoClass.LastChild)) text.Append(":last-child");
        if (Has(PseudoClass.Disabled)) text.Append(":disabled");
        if (Has(PseudoClass.Enabled)) text.Append(":enabled");
        if (Has(PseudoClass.PlaceholderShown)) text.Append(":placeholder-shown");
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

    /// <summary>CSS specificity as ids × 10000 + (classes, attributes and pseudo classes) × 100 + types.</summary>
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
            classes += compound.Classes.Count + compound.Attributes.Count + BitOperations.PopCount((uint)compound.Pseudo);
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
        private static readonly Dictionary<string, PseudoClass> PseudoClasses = new(StringComparer.Ordinal)
        {
            ["focus"] = PseudoClass.Focus,
            ["focus-within"] = PseudoClass.FocusWithin,
            ["root"] = PseudoClass.Root,
            ["first-child"] = PseudoClass.FirstChild,
            ["last-child"] = PseudoClass.LastChild,
            ["disabled"] = PseudoClass.Disabled,
            ["enabled"] = PseudoClass.Enabled,
            ["placeholder-shown"] = PseudoClass.PlaceholderShown,
        };

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
            var attributes = new List<AttributeSelector>();
            var pseudo = PseudoClass.None;

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
                    case '[':
                        _position++;
                        attributes.Add(ReadAttribute());
                        break;
                    case ':':
                        _position++;
                        pseudo |= ReadPseudoClass();
                        break;
                    case '*':
                        if (!isFirstPart) throw Error("a type must come first in a compound");
                        type = "*";
                        _position++;
                        break;
                    case var c when IsNameChar(c):
                        if (!isFirstPart) throw Error("a type must come first in a compound");
                        type = ReadName("type").ToLowerInvariant();
                        break;
                    default:
                        throw Error($"unexpected '{Current}'");
                }
            }
            return new CompoundSelector(type, classes, id, attributes, pseudo, combinator);
        }

        private PseudoClass ReadPseudoClass()
        {
            if (!AtEnd && Current == ':') throw Error("pseudo-elements are not supported");
            var name = ReadName("pseudo-class");
            if (PseudoClasses.TryGetValue(name, out var pseudoClass)) return pseudoClass;
            throw Error($"':{name}' is not a supported pseudo-class ({string.Join(", ", PseudoClasses.Keys)})");
        }

        /// <summary>Reads <c>name]</c> or <c>name=value]</c>, the value bare or quoted.</summary>
        private AttributeSelector ReadAttribute()
        {
            SkipWhitespace();
            var name = ReadName("attribute");
            SkipWhitespace();
            if (AtEnd) throw Error("'[' is not closed");
            if (Current == ']')
            {
                _position++;
                return new AttributeSelector(name, null);
            }
            if (Current != '=') throw Error("only [name] and [name=value] attribute selectors are supported");

            _position++;
            SkipWhitespace();
            var value = ReadAttributeValue();
            SkipWhitespace();
            if (AtEnd || Current != ']') throw Error("'[' is not closed");
            _position++;
            return new AttributeSelector(name, value);
        }

        private string ReadAttributeValue()
        {
            if (AtEnd || Current is not ('"' or '\'')) return ReadWhile(c => c != ']' && !char.IsWhiteSpace(c));

            var quote = Current;
            _position++;
            var value = ReadWhile(c => c != quote);
            if (AtEnd) throw Error("an attribute value is not closed");
            _position++;
            return value;
        }

        private string ReadName(string kind)
        {
            var start = _position;
            var name = ReadWhile(IsNameChar);
            if (name.Length == 0) throw Error($"a {kind} name is missing at position {start}");
            return name;
        }

        private string ReadWhile(Func<char, bool> predicate)
        {
            var start = _position;
            while (!AtEnd && predicate(Current)) _position++;
            return text[start.._position];
        }

        private void SkipWhitespace() => ReadWhile(char.IsWhiteSpace);

        private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '-' or '_';

        private FormatException Error(string problem) => new($"'{text}': {problem}.");
    }
}
