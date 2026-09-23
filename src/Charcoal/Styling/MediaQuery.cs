using System.Globalization;

namespace Charcoal.Styling;

/// <summary>What <c>prefers-color-scheme</c> answers: the terminal's background, dark or light.</summary>
public enum ColorScheme { Dark, Light }

/// <summary>
/// The terminal as a media query sees it. The app keeps the size and the
/// colour scheme current; an app can set the rest.
/// </summary>
public sealed record MediaEnvironment
{
    /// <summary>The viewport, in cells.</summary>
    public int Width { get; init; } = 80;
    public int Height { get; init; } = 24;

    /// <summary>From the terminal's background colour, or set by the app; dark until known.</summary>
    public ColorScheme ColorScheme { get; init; } = ColorScheme.Dark;

    /// <summary>Bits per colour component: 8 with truecolour, 4 with 256 colours, 2 with sixteen.</summary>
    public int ColorBits { get; init; } = 8;

    /// <summary>Whether the mouse is on, which makes <c>pointer</c> fine rather than none.</summary>
    public bool Pointer { get; init; } = true;

    public bool ReducedMotion { get; init; }

    /// <summary>
    /// The cell's size in device pixels, as the terminal reports it, or 0
    /// until it does. A cell is the CSS pixel here, so <c>resolution</c> in
    /// <c>dppx</c> is the cell width.
    /// </summary>
    public int CellPixelWidth { get; init; }
    public int CellPixelHeight { get; init; }

    public static readonly MediaEnvironment Default = new();

    /// <summary>Wider than tall; a square viewport is portrait, as in CSS.</summary>
    public bool Landscape => Width > Height;
}

/// <summary>CSS's three-valued media logic, where an unknown feature is neither true nor false.</summary>
public enum MediaResult { False, True, Unknown }

/// <summary>
/// A media query list as <c>@media</c> writes it, in the Media Queries Level 4
/// grammar: media types, <c>not</c>, <c>only</c>, <c>and</c>, <c>or</c>,
/// parentheses, and boolean, plain and range features such as
/// <c>(60 &lt;= width &lt; 120)</c>. Lengths are in cells.
/// </summary>
/// <remarks>
/// A feature this library does not know, or a length in <c>px</c>, is unknown
/// and makes its query false, as a browser treats features it does not
/// support. The features are <c>width</c>, <c>height</c>, <c>aspect-ratio</c>,
/// <c>orientation</c>, <c>prefers-color-scheme</c>, <c>color</c>,
/// <c>monochrome</c> (0), <c>grid</c> (1, since a terminal is a grid device),
/// <c>hover</c> and <c>any-hover</c> (none), <c>pointer</c> and
/// <c>any-pointer</c> (fine with the mouse on), <c>prefers-reduced-motion</c>,
/// <c>prefers-contrast</c> (no-preference), <c>forced-colors</c> (none),
/// <c>display-mode</c> (fullscreen), <c>scripting</c> (enabled),
/// <c>update</c> (fast) and <c>resolution</c> in <c>dppx</c> or <c>x</c>,
/// which is unknown until the terminal reports its cell size. Inside
/// <c>@container</c> the same grammar runs against the container's content
/// box, where <c>inline-size</c> and <c>block-size</c> name its width and height.
/// </remarks>
public sealed class MediaQueryList
{
    private static readonly Node True = new ConstNode(MediaResult.True);
    private static readonly Node False = new ConstNode(MediaResult.False);
    private static readonly Node Unknown = new ConstNode(MediaResult.Unknown);

    private readonly Node _root;
    private MediaEnvironment? _lastEnvironment;
    private bool _lastResult;

    private MediaQueryList(Node root, string text)
    {
        _root = root;
        Text = text;
    }

    public string Text { get; }

    /// <summary>Whether any query in the list is true.</summary>
    public bool Matches(MediaEnvironment environment)
    {
        if (!ReferenceEquals(environment, _lastEnvironment))
        {
            _lastEnvironment = environment;
            _lastResult = _root.Evaluate(environment) == MediaResult.True;
        }
        return _lastResult;
    }

    public MediaResult Evaluate(MediaEnvironment environment) => _root.Evaluate(environment);

    /// <summary>A list that holds when both hold, which is what nesting one <c>@media</c> in another means.</summary>
    public static MediaQueryList And(MediaQueryList outer, MediaQueryList inner) =>
        new(new AndNode([outer._root, inner._root]), $"{outer.Text} and {inner.Text}");

    /// <exception cref="FormatException">The query is malformed. An unknown feature is not an error.</exception>
    public static MediaQueryList Parse(string text)
    {
        var trimmed = text.Trim();
        var parser = new Parser(Tokenize(trimmed), trimmed);
        if (parser.AtEnd) throw new FormatException("'@media' has no query.");

        var queries = new List<Node> { parser.Query() };
        while (!parser.AtEnd)
        {
            parser.Expect(",");
            queries.Add(parser.Query());
        }
        return new MediaQueryList(queries.Count == 1 ? queries[0] : new OrNode(queries), trimmed);
    }

    public override string ToString() => Text;

    private abstract class Node
    {
        public abstract MediaResult Evaluate(MediaEnvironment environment);
    }

    private sealed class ConstNode(MediaResult value) : Node
    {
        public override MediaResult Evaluate(MediaEnvironment environment) => value;
    }

    private sealed class NotNode(Node inner) : Node
    {
        public override MediaResult Evaluate(MediaEnvironment environment) => inner.Evaluate(environment) switch
        {
            MediaResult.True => MediaResult.False,
            MediaResult.False => MediaResult.True,
            _ => MediaResult.Unknown,
        };
    }

    private sealed class AndNode(IReadOnlyList<Node> parts) : Node
    {
        public override MediaResult Evaluate(MediaEnvironment environment)
        {
            var result = MediaResult.True;
            foreach (var part in parts)
            {
                var partResult = part.Evaluate(environment);
                if (partResult == MediaResult.False) return MediaResult.False;
                if (partResult == MediaResult.Unknown) result = MediaResult.Unknown;
            }
            return result;
        }
    }

    private sealed class OrNode(IReadOnlyList<Node> parts) : Node
    {
        public override MediaResult Evaluate(MediaEnvironment environment)
        {
            var result = MediaResult.False;
            foreach (var part in parts)
            {
                var partResult = part.Evaluate(environment);
                if (partResult == MediaResult.True) return MediaResult.True;
                if (partResult == MediaResult.Unknown) result = MediaResult.Unknown;
            }
            return result;
        }
    }

    private sealed class FeatureNode(Func<MediaEnvironment, MediaResult> test) : Node
    {
        public override MediaResult Evaluate(MediaEnvironment environment) => test(environment);
    }

    private readonly record struct Token(string Kind, string Text);

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c is '(' or ')' or ':' or ',' or '/')
            {
                tokens.Add(new Token(c.ToString(), c.ToString()));
                i++;
            }
            else if (c is '<' or '>' or '=')
            {
                var isTwoCharacters = c != '=' && next == '=';
                var length = isTwoCharacters ? 2 : 1;
                tokens.Add(new Token("op", text.Substring(i, length)));
                i += length;
            }
            else if (char.IsDigit(c) || c == '.' || (c == '-' && char.IsDigit(next)))
            {
                var start = i++;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '.' or '%')) i++;
                tokens.Add(new Token("number", text[start..i]));
            }
            else if (char.IsLetter(c) || c is '-' or '_')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '-' or '_')) i++;
                tokens.Add(new Token("ident", text[start..i]));
            }
            else
            {
                throw new FormatException($"'{text}': unexpected '{c}' in a media query.");
            }
        }
        return tokens;
    }

    private sealed class Parser(List<Token> tokens, string source)
    {
        private int _position;

        public bool AtEnd => _position >= tokens.Count;

        private Token? Peek(int ahead = 0) => _position + ahead < tokens.Count ? tokens[_position + ahead] : null;

        private bool PeekWord(string word) =>
            Peek() is { Kind: "ident" } token && token.Text.Equals(word, StringComparison.OrdinalIgnoreCase);

        private bool TryConsumeWord(string word)
        {
            if (!PeekWord(word)) return false;
            _position++;
            return true;
        }

        private Token Next()
        {
            if (AtEnd) throw Error("the media query ends early");
            return tokens[_position++];
        }

        public void Expect(string kind)
        {
            var token = Next();
            if (token.Kind != kind && token.Text != kind) throw Error($"expected '{kind}', found '{token.Text}'");
        }

        /// <summary><c>[not|only] type [and condition]</c>, or a bare condition.</summary>
        public Node Query()
        {
            var startsWithType = Peek() is { Kind: "ident" } && !PeekWord("not") && !PeekWord("only");
            if (startsWithType) return TypedQuery(negate: false);
            if (TryConsumeWord("only")) return TypedQuery(negate: false);
            if (PeekWord("not") && Peek(1) is { Kind: "ident" })
            {
                _position++;
                return TypedQuery(negate: true);
            }
            return Condition(allowOr: true);
        }

        private Node TypedQuery(bool negate)
        {
            var type = Next();
            if (type.Kind != "ident") throw Error($"expected a media type, found '{type.Text}'");

            var parts = new List<Node> { MediaType(type.Text) };
            while (TryConsumeWord("and"))
            {
                parts.Add(InParens());
            }
            var node = parts.Count == 1 ? parts[0] : new AndNode(parts);
            return negate ? new NotNode(node) : node;
        }

        /// <summary>A terminal is a screen; print, speech and the retired types match nothing.</summary>
        private static Node MediaType(string name) => name.ToLowerInvariant() is "all" or "screen" ? True : False;

        /// <summary><c>not (…)</c>, or parenthesized parts joined by <c>and</c> or by <c>or</c>.</summary>
        private Node Condition(bool allowOr)
        {
            if (TryConsumeWord("not")) return new NotNode(InParens());

            var first = InParens();
            if (PeekWord("and")) return Joined(first, "and", "or", parts => new AndNode(parts));
            if (!PeekWord("or")) return first;
            if (!allowOr) throw Error("'or' is not allowed after a media type");
            return Joined(first, "or", "and", parts => new OrNode(parts));
        }

        private Node Joined(Node first, string joiner, string other, Func<List<Node>, Node> combine)
        {
            var parts = new List<Node> { first };
            while (TryConsumeWord(joiner))
            {
                parts.Add(InParens());
            }
            if (PeekWord(other)) throw Error("'and' and 'or' cannot be mixed without parentheses");
            return combine(parts);
        }

        /// <summary><c>( condition )</c> or <c>( feature )</c>.</summary>
        private Node InParens()
        {
            Expect("(");
            var isNestedCondition = PeekWord("not") || Peek() is { Kind: "(" };
            var node = isNestedCondition ? Condition(allowOr: true) : Feature();
            Expect(")");
            return node;
        }

        /// <summary>A boolean feature, a <c>name: value</c> feature, or a range.</summary>
        private Node Feature()
        {
            var first = Next();
            if (first.Kind == "number") return RangeFromValue(first.Text);
            if (first.Kind != "ident") throw Error($"expected a feature, found '{first.Text}'");

            var feature = first.Text.ToLowerInvariant();
            switch (Peek()?.Kind)
            {
                case ")":
                    return Boolean(feature);
                case ":":
                    _position++;
                    return Plain(feature, ReadValue());
                case "op":
                    var op = NextOp();
                    return Compare(feature, op, ReadValue());
                default:
                    throw Error($"unexpected '{Peek()?.Text}' after '{first.Text}'");
            }
        }

        /// <summary><c>value op name [op value]</c>, such as <c>60 &lt;= width &lt; 120</c>.</summary>
        private Node RangeFromValue(string left)
        {
            var op = NextOp();
            var name = Next();
            if (name.Kind != "ident") throw Error($"expected a feature name after '{op}'");

            var lower = Compare(name.Text, Flip(op), left);
            if (Peek() is not { Kind: "op" }) return lower;

            var upperOp = NextOp();
            var right = Next();
            if (right.Kind != "number") throw Error($"expected a value after '{upperOp}'");
            return new AndNode([lower, Compare(name.Text, upperOp, right.Text)]);
        }

        private string NextOp()
        {
            var token = Next();
            if (token.Kind != "op") throw Error($"expected a comparison, found '{token.Text}'");
            return token.Text;
        }

        /// <summary>An identifier, a number, or a ratio <c>a / b</c>.</summary>
        private string ReadValue()
        {
            var token = Next();
            if (token.Kind is not ("ident" or "number")) throw Error($"expected a value, found '{token.Text}'");
            if (token.Kind != "number" || Peek() is not { Kind: "/" }) return token.Text;

            _position++;
            var denominator = Next();
            if (denominator.Kind != "number") throw Error("expected a number after '/'");
            return token.Text + "/" + denominator.Text;
        }

        /// <summary>Turns <c>a &lt; width</c> into <c>width &gt; a</c>.</summary>
        private static string Flip(string op) => op switch
        {
            "<" => ">",
            "<=" => ">=",
            ">" => "<",
            ">=" => "<=",
            _ => op,
        };

        private FormatException Error(string problem) => new($"'{source}': {problem}.");
    }

    private static readonly HashSet<string> Numeric = new(StringComparer.Ordinal)
    {
        "width", "height", "inline-size", "block-size", "aspect-ratio", "color", "monochrome", "grid", "resolution",
    };

    /// <summary><c>(feature)</c>: true when the feature's value is not zero or none.</summary>
    private static Node Boolean(string feature) => feature switch
    {
        "width" or "height" or "inline-size" or "block-size" or "aspect-ratio" or "color" or "grid" => True,
        "resolution" => new FeatureNode(env => env.CellPixelWidth > 0 ? MediaResult.True : MediaResult.Unknown),
        "monochrome" or "hover" or "any-hover" => False,
        "pointer" or "any-pointer" => new FeatureNode(env => Truth(env.Pointer)),
        "orientation" or "prefers-color-scheme" or "display-mode" or "scripting" or "update" => True,
        "prefers-reduced-motion" => new FeatureNode(env => Truth(env.ReducedMotion)),
        "prefers-contrast" or "forced-colors" or "prefers-reduced-transparency" => False,
        _ => Unknown,
    };

    /// <summary><c>(name: value)</c>, including the older <c>min-</c> and <c>max-</c> forms.</summary>
    private static Node Plain(string feature, string value)
    {
        if (feature.StartsWith("min-", StringComparison.Ordinal)) return Compare(feature[4..], ">=", value);
        if (feature.StartsWith("max-", StringComparison.Ordinal)) return Compare(feature[4..], "<=", value);
        if (Numeric.Contains(feature)) return Compare(feature, "=", value);

        var keyword = value.ToLowerInvariant();
        return feature switch
        {
            "orientation" => keyword switch
            {
                "landscape" => new FeatureNode(env => Truth(env.Landscape)),
                "portrait" => new FeatureNode(env => Truth(!env.Landscape)),
                _ => Unknown,
            },
            "prefers-color-scheme" => keyword switch
            {
                "dark" => new FeatureNode(env => Truth(env.ColorScheme == ColorScheme.Dark)),
                "light" => new FeatureNode(env => Truth(env.ColorScheme == ColorScheme.Light)),
                _ => Unknown,
            },
            "pointer" or "any-pointer" => keyword switch
            {
                "none" => new FeatureNode(env => Truth(!env.Pointer)),
                "fine" => new FeatureNode(env => Truth(env.Pointer)),
                "coarse" => False,
                _ => Unknown,
            },
            "prefers-reduced-motion" => keyword switch
            {
                "reduce" => new FeatureNode(env => Truth(env.ReducedMotion)),
                "no-preference" => new FeatureNode(env => Truth(!env.ReducedMotion)),
                _ => Unknown,
            },
            "hover" or "any-hover" => Fixed(keyword, "none", "hover"),
            "prefers-contrast" => Fixed(keyword, "no-preference", "more", "less", "custom"),
            "forced-colors" => Fixed(keyword, "none", "active"),
            "prefers-reduced-transparency" => Fixed(keyword, "no-preference", "reduce"),
            "display-mode" => Fixed(keyword, "fullscreen", "standalone", "minimal-ui", "browser", "picture-in-picture", "window-controls-overlay"),
            "scripting" => Fixed(keyword, "enabled", "none", "initial-only"),
            "update" => Fixed(keyword, "fast", "slow", "none"),
            _ => Unknown,
        };
    }

    /// <summary>A feature with a fixed value here: true for it, false for the feature's other values, else unknown.</summary>
    private static Node Fixed(string keyword, string value, params string[] otherValues)
    {
        if (keyword == value) return True;
        return otherValues.Contains(keyword) ? False : Unknown;
    }

    /// <summary><c>name op value</c> for a numeric feature. A length in anything but cells is unknown.</summary>
    private static Node Compare(string feature, string op, string value)
    {
        if (!Numeric.Contains(feature)) return Unknown;
        if (feature == "aspect-ratio")
        {
            if (!TryRatio(value, out var numerator, out var denominator)) return Unknown;
            // width / height op numerator / denominator, without dividing.
            return new FeatureNode(env => Truth(Holds(op, env.Width * denominator, numerator * env.Height)));
        }

        if (feature == "resolution")
        {
            if (!TryResolution(value, out var dppx)) return Unknown;
            return new FeatureNode(env => env.CellPixelWidth > 0 ? Truth(Holds(op, env.CellPixelWidth, dppx)) : MediaResult.Unknown);
        }

        if (!TryCells(value, out var cells)) return Unknown;
        return feature switch
        {
            "width" or "inline-size" => new FeatureNode(env => Truth(Holds(op, env.Width, cells))),
            "height" or "block-size" => new FeatureNode(env => Truth(Holds(op, env.Height, cells))),
            "color" => new FeatureNode(env => Truth(Holds(op, env.ColorBits, cells))),
            "monochrome" => Const(Holds(op, 0, cells)),
            "grid" => Const(Holds(op, 1, cells)),
            _ => Unknown,
        };
    }

    private static bool Holds(string op, double left, double right) => op switch
    {
        "<" => left < right,
        "<=" => left <= right,
        ">" => left > right,
        ">=" => left >= right,
        _ => Math.Abs(left - right) < 1e-9,
    };

    private static readonly string[] CellUnits = ["rem", "ch", "em", "lh"];

    private static bool TryCells(string text, out double cells)
    {
        var lower = text.ToLowerInvariant();
        var unit = CellUnits.FirstOrDefault(u => lower.EndsWith(u, StringComparison.Ordinal));
        var number = unit is null ? lower : lower[..^unit.Length];
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out cells);
    }

    /// <summary>
    /// Reads <c>Ndppx</c> or <c>Nx</c>, device pixels per cell. <c>dpi</c> and
    /// <c>dpcm</c> are not supported, since a terminal has no physical size.
    /// </summary>
    private static bool TryResolution(string text, out double dppx)
    {
        dppx = 0;
        var lower = text.ToLowerInvariant();
        string number;
        if (lower.EndsWith("dppx", StringComparison.Ordinal))
        {
            number = lower[..^"dppx".Length];
        }
        else if (lower.EndsWith('x') && !lower.EndsWith("px", StringComparison.Ordinal))
        {
            number = lower[..^1];
        }
        else
        {
            return false;
        }
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out dppx);
    }

    private static bool TryRatio(string text, out double numerator, out double denominator)
    {
        numerator = 0;
        denominator = 1;
        var slash = text.IndexOf('/');
        if (slash < 0) return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out numerator);

        return double.TryParse(text[..slash], NumberStyles.Float, CultureInfo.InvariantCulture, out numerator)
            && double.TryParse(text[(slash + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out denominator)
            && denominator > 0;
    }

    private static MediaResult Truth(bool value) => value ? MediaResult.True : MediaResult.False;

    private static Node Const(bool value) => value ? True : False;
}

/// <summary>
/// An <c>@container</c> prelude: an optional container name and a condition,
/// evaluated against the content box of the nearest ancestor with a
/// <c>container-type</c> (and that name, when one is given).
/// </summary>
public sealed class ContainerQuery
{
    private ContainerQuery(string? name, MediaQueryList condition, string text)
    {
        Name = name;
        Condition = condition;
        Text = text;
    }

    /// <summary>The container name asked for, or null for the nearest container.</summary>
    public string? Name { get; }

    public MediaQueryList Condition { get; }

    public string Text { get; }

    /// <summary>Parses <c>[name] condition</c>.</summary>
    /// <exception cref="FormatException">The prelude is malformed.</exception>
    public static ContainerQuery Parse(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) throw new FormatException("'@container' has no condition.");

        var startsWithCondition = trimmed.StartsWith('(') || trimmed.StartsWith("not", StringComparison.OrdinalIgnoreCase);
        if (startsWithCondition) return new ContainerQuery(null, MediaQueryList.Parse(trimmed), trimmed);

        var nameEnd = trimmed.IndexOfAny([' ', '\t', '(']);
        if (nameEnd < 0) throw new FormatException($"'@container {trimmed}': a name needs a condition after it.");
        var condition = MediaQueryList.Parse(trimmed[nameEnd..].Trim());
        return new ContainerQuery(trimmed[..nameEnd], condition, trimmed);
    }

    /// <summary>Whether the container has the name this query asks for, if any.</summary>
    public bool Addresses(Layout.Style container) =>
        Name is null || container.ContainerNames.Contains(Name, StringComparer.Ordinal);

    public override string ToString() => Text;
}

/// <summary>
/// <c>@supports</c>, decided at parse time. <c>(property: value)</c> holds when
/// the property is supported and the value parses, <c>selector(…)</c> when the
/// selector parses, and <c>not</c>, <c>and</c>, <c>or</c> and parentheses
/// combine them.
/// </summary>
public static class SupportsCondition
{
    public static bool Evaluate(string text)
    {
        var trimmed = text.Trim();
        return new SupportsReader(trimmed, trimmed).ReadAll();
    }

    private sealed class SupportsReader(string text, string source)
    {
        private int _position;

        public bool ReadAll()
        {
            var result = Or();
            SkipSpace();
            if (_position < text.Length) throw Error($"unexpected '{text[_position..]}'");
            return result;
        }

        private bool Or()
        {
            var result = And();
            while (TryConsumeWord("or"))
            {
                result |= And();
            }
            return result;
        }

        private bool And()
        {
            var result = Not();
            while (TryConsumeWord("and"))
            {
                result &= Not();
            }
            return result;
        }

        private bool Not() => TryConsumeWord("not") ? !Not() : Group();

        private bool Group()
        {
            SkipSpace();
            if (text.AsSpan(_position).StartsWith("selector(", StringComparison.OrdinalIgnoreCase))
            {
                return SelectorFunction();
            }
            if (_position >= text.Length || text[_position] != '(') throw Error($"expected '(' at position {_position}");

            var close = MatchingParen(_position);
            var inner = text[(_position + 1)..close].Trim();
            _position = close + 1;
            return IsNestedCondition(inner) ? new SupportsReader(inner, source).ReadAll() : Declaration(inner);
        }

        private bool SelectorFunction()
        {
            var open = _position + "selector".Length;
            var close = MatchingParen(open);
            var selector = text[(open + 1)..close];
            _position = close + 1;
            try
            {
                Selector.Parse(selector);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary>Whether a parenthesized group holds a condition rather than a declaration.</summary>
        private static bool IsNestedCondition(string inner)
        {
            if (inner.StartsWith('(')) return true;
            if (!inner.StartsWith("not", StringComparison.OrdinalIgnoreCase) || inner.Length <= 3) return false;
            var afterNot = inner[3];
            return !char.IsLetterOrDigit(afterNot) && afterNot != '-';
        }

        private static bool Declaration(string declaration)
        {
            var colon = declaration.IndexOf(':');
            if (colon < 0) return false;

            var name = declaration[..colon].Trim();
            var value = declaration[(colon + 1)..].Trim();
            if (Layout.StyleParser.IsCustomProperty(name)) return true;
            if (!Layout.StyleParser.IsProperty(name)) return false;
            if (value.Contains("var(", StringComparison.OrdinalIgnoreCase)) return true;
            try
            {
                Layout.StyleParser.Apply(Layout.Style.Default, name, value);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private bool TryConsumeWord(string word)
        {
            SkipSpace();
            var end = _position + word.Length;
            if (end > text.Length || !text.AsSpan(_position, word.Length).Equals(word, StringComparison.OrdinalIgnoreCase)) return false;
            if (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '-')) return false;
            _position = end;
            return true;
        }

        private void SkipSpace()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position])) _position++;
        }

        private int MatchingParen(int open)
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
            throw Error("'(' is not closed");
        }

        private FormatException Error(string problem) => new($"'@supports {source}': {problem}.");
    }
}
