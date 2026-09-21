using System.Net;
using System.Text;

namespace SlopTui.Components;

/// <summary>
/// Parses the static HTML that Razor folds into a single markup frame, the
/// job the DOM parser does for the browser renderer. It reads elements with
/// quoted, unquoted or bare attributes, void and self-closing tags, comments,
/// and text with entities.
/// </summary>
internal static class MarkupParser
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "br", "hr", "img", "input", "meta", "link", "area", "base", "col", "embed", "source", "track", "wbr",
    };

    /// <summary>
    /// Collapses whitespace containing a line break to one space, or to
    /// nothing at the start of a parent's content or before its end, as HTML
    /// does. Text that is only whitespace is kept, so markup indentation can
    /// still be told apart from a deliberate space. A line break is <c>&lt;br&gt;</c>.
    /// </summary>
    internal static string CollapseNewlines(string text, bool first = true, bool last = true)
    {
        if (text.AsSpan().IndexOfAny('\n', '\r') < 0) return text;
        if (string.IsNullOrWhiteSpace(text)) return text;

        var result = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                result.Append(text[i]);
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            var whitespace = text.AsSpan(start, i - start);
            if (whitespace.IndexOfAny('\n', '\r') < 0)
            {
                result.Append(whitespace);
                continue;
            }

            var atStart = start == 0 && first;
            var atEnd = i == text.Length && last;
            if (!atStart && !atEnd) result.Append(' ');
        }
        return result.ToString();
    }

    /// <summary>The top-level nodes of the fragment, in order.</summary>
    public static List<HostNode> Parse(string markup, Func<string, HostElement> newElement) =>
        new MarkupReader(markup, newElement).ReadAll();

    private sealed class MarkupReader(string markup, Func<string, HostElement> newElement)
    {
        private readonly List<HostNode> _roots = [];
        private readonly Stack<HostElement> _open = new();
        private readonly StringBuilder _text = new();
        private int _position;

        private bool AtEnd => _position >= markup.Length;
        private char Current => markup[_position];

        public List<HostNode> ReadAll()
        {
            while (!AtEnd)
            {
                if (Current != '<')
                {
                    _text.Append(Current);
                    _position++;
                }
                else if (markup.AsSpan(_position).StartsWith("<!--"))
                {
                    FlushText();
                    SkipComment();
                }
                else if (NextIs('/'))
                {
                    FlushText(beforeClose: true);
                    if (!ReadClosingTag()) break;
                }
                else if (NextIs(char.IsLetter) || NextIs('_'))
                {
                    FlushText();
                    ReadElement();
                }
                else
                {
                    _text.Append(Current);
                    _position++;
                }
            }
            FlushText(beforeClose: true);
            return _roots;
        }

        private bool NextIs(char c) => _position + 1 < markup.Length && markup[_position + 1] == c;

        private bool NextIs(Func<char, bool> predicate) => _position + 1 < markup.Length && predicate(markup[_position + 1]);

        private void SkipComment()
        {
            var close = markup.IndexOf("-->", _position + 4, StringComparison.Ordinal);
            _position = close < 0 ? markup.Length : close + 3;
        }

        /// <summary>Closes up to the matching open element; returns false if the tag never ends.</summary>
        private bool ReadClosingTag()
        {
            var close = markup.IndexOf('>', _position);
            if (close < 0) return false;

            var name = markup[(_position + 2)..close].Trim();
            if (_open.Any(e => IsNamed(e, name))) CloseUpTo(name);
            _position = close + 1;
            return true;
        }

        /// <summary>Closes the named element and every element still open inside it.</summary>
        private void CloseUpTo(string name)
        {
            HostElement closed;
            do
            {
                closed = _open.Pop();
            }
            while (!IsNamed(closed, name));
        }

        private static bool IsNamed(HostElement element, string name) =>
            string.Equals(element.Name, name, StringComparison.OrdinalIgnoreCase);

        private void ReadElement()
        {
            _position++;
            var name = ReadWhile(c => !char.IsWhiteSpace(c) && c is not ('>' or '/'));
            var element = newElement(name.ToLowerInvariant());
            var selfClosing = ReadAttributes(element);
            Add(element);
            if (!selfClosing && !VoidElements.Contains(element.Name)) _open.Push(element);
        }

        /// <summary>Reads attributes up to the end of the tag; returns whether the tag closed itself.</summary>
        private bool ReadAttributes(HostElement element)
        {
            var selfClosing = false;
            while (true)
            {
                SkipWhitespace();
                if (AtEnd) return selfClosing;
                if (Current == '>')
                {
                    _position++;
                    return selfClosing;
                }
                if (Current == '/')
                {
                    selfClosing = true;
                    _position++;
                    continue;
                }

                var name = ReadWhile(c => !char.IsWhiteSpace(c) && c is not ('=' or '>' or '/'));
                SkipWhitespace();
                // A bare attribute is present, as in Blazor.
                object value = true;
                if (!AtEnd && Current == '=')
                {
                    _position++;
                    SkipWhitespace();
                    value = WebUtility.HtmlDecode(ReadAttributeValue());
                }
                if (name.Length > 0) element.SetAttribute(name, value, 0);
            }
        }

        private string ReadAttributeValue()
        {
            if (AtEnd || Current is not ('"' or '\'')) return ReadWhile(c => !char.IsWhiteSpace(c) && c != '>');

            var quote = Current;
            _position++;
            var value = ReadWhile(c => c != quote);
            if (!AtEnd) _position++;
            return value;
        }

        private string ReadWhile(Func<char, bool> predicate)
        {
            var start = _position;
            while (!AtEnd && predicate(Current)) _position++;
            return markup[start.._position];
        }

        private void SkipWhitespace() => ReadWhile(char.IsWhiteSpace);

        private void FlushText(bool beforeClose = false)
        {
            if (_text.Length == 0) return;
            var first = _open.TryPeek(out var parent) ? parent.Children.Count == 0 : _roots.Count == 0;
            var text = CollapseNewlines(_text.ToString(), first, beforeClose);
            Add(new HostTextNode { Text = WebUtility.HtmlDecode(text) });
            _text.Clear();
        }

        private void Add(HostNode node)
        {
            if (_open.TryPeek(out var parent))
            {
                parent.InsertChild(parent.Children.Count, node);
            }
            else
            {
                _roots.Add(node);
            }
        }
    }
}
