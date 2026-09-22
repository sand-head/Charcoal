using System.Globalization;
using System.Text;
using SlopTui.Rendering;

namespace SlopTui.Components;

/// <summary>A visual line of an editor's text. A hard line ends at a newline or the end of the text.</summary>
public readonly record struct TextLine(int Start, int End, bool Hard)
{
    public int Length => End - Start;
}

/// <summary>
/// The text and caret of an <c>input</c> or <c>textarea</c>, independent of
/// the terminal.
/// </summary>
/// <remarks>
/// The caret always sits on a grapheme boundary, so a cluster such as an
/// emoji with a skin tone moves and deletes as one. Visual lines break at
/// newlines and, when wrapping, after the last space that fits or at the
/// width. Masked text shows each cluster as one column.
/// </remarks>
public sealed class TextEditor
{
    private readonly StringBuilder _text = new();
    private string? _cached = "";
    private int _caret;
    private int _goalColumn = -1;
    private (string Text, int Width, bool Wrap, IReadOnlyList<TextLine> Lines)? _lines;

    /// <summary>Whether the text may contain newlines.</summary>
    public bool Multiline { get; set; }

    /// <summary>The maximum length in code points, as <c>maxlength</c>.</summary>
    public int? MaxLength { get; set; }

    /// <summary>Whether the text is masked, as in a password field.</summary>
    public bool Masked { get; set; }

    public string Text => _cached ??= _text.ToString();

    /// <summary>The caret's index into <see cref="Text"/>, on a grapheme boundary.</summary>
    public int Caret => _caret;

    public int Length => _text.Length;

    public bool IsEmpty => _text.Length == 0;

    /// <summary>
    /// Replaces the text and moves the caret to the end. An unchanged text
    /// leaves the caret where it was.
    /// </summary>
    /// <returns>Whether the text changed.</returns>
    public bool SetText(string text)
    {
        text = Sanitize(text);
        if (Text == text) return false;
        _text.Clear();
        _text.Append(text);
        _caret = _text.Length;
        Changed();
        return true;
    }

    /// <summary>Moves the caret to the last grapheme boundary at or before <paramref name="index"/>.</summary>
    public void MoveTo(int index)
    {
        index = Math.Clamp(index, 0, _text.Length);
        _caret = 0;
        foreach (var length in ElementLengths(Text))
        {
            if (_caret + length > index) break;
            _caret += length;
        }
        _goalColumn = -1;
    }

    /// <summary>Inserts text at the caret, truncated to fit <see cref="MaxLength"/>.</summary>
    /// <returns>Whether any text was inserted.</returns>
    public bool Insert(string text)
    {
        text = Sanitize(text);
        if (text.Length == 0) return false;
        if (MaxLength is { } max)
        {
            var room = max - Text.EnumerateRunes().Count();
            if (room <= 0) return false;
            text = TakeRunes(text, room);
            if (text.Length == 0) return false;
        }
        _text.Insert(_caret, text);
        _caret += text.Length;
        Changed();
        return true;
    }

    public bool Backspace() => Remove(PreviousBoundary(_caret), _caret);

    public bool Delete() => Remove(_caret, NextBoundary(_caret));

    /// <summary>Deletes the word before the caret and the spaces after it.</summary>
    public bool DeleteWordBackward() => Remove(WordStart(_caret), _caret);

    public bool DeleteWordForward() => Remove(_caret, WordEnd(_caret));

    public bool DeleteToLineStart() => Remove(LineStartOf(_caret), _caret);

    /// <summary>Deletes to the end of the line, or the newline itself when the caret is already there.</summary>
    public bool DeleteToLineEnd()
    {
        var end = LineEndOf(_caret);
        if (end == _caret && end < _text.Length)
        {
            end++;
        }
        return Remove(_caret, end);
    }

    public bool MoveLeft(bool byWord = false) => Move(byWord ? WordStart(_caret) : PreviousBoundary(_caret));

    public bool MoveRight(bool byWord = false) => Move(byWord ? WordEnd(_caret) : NextBoundary(_caret));

    public bool MoveToLineStart() => Move(LineStartOf(_caret));

    public bool MoveToLineEnd() => Move(LineEndOf(_caret));

    public bool MoveToStart() => Move(0);

    public bool MoveToEnd() => Move(_text.Length);

    /// <summary>
    /// Moves the caret down <paramref name="delta"/> visual lines, or up when
    /// negative, keeping its column across shorter lines.
    /// </summary>
    /// <returns>False when there is no line to move to.</returns>
    public bool MoveVertical(int delta, int width, bool wrap)
    {
        var lines = Lines(width, wrap);
        var (column, row) = CaretPosition(width, wrap);
        var target = row + delta;
        if (target < 0 || target >= lines.Count) return false;
        var goal = _goalColumn >= 0 ? _goalColumn : column;
        _caret = IndexAt(width, wrap, goal, target);
        _goalColumn = goal;
        return true;
    }

    /// <summary>The visual lines of the text at a width, excluding the newlines that end them.</summary>
    public IReadOnlyList<TextLine> Lines(int width, bool wrap)
    {
        var text = Text;
        if (_lines is { } cached && ReferenceEquals(cached.Text, text) && cached.Width == width && cached.Wrap == wrap)
        {
            return cached.Lines;
        }

        var lines = new List<TextLine>();
        var start = 0;
        while (true)
        {
            var newline = text.IndexOf('\n', start);
            var end = newline < 0 ? text.Length : newline;
            if (wrap && width > 0)
            {
                WrapLine(text, start, end, width, lines);
            }
            else
            {
                lines.Add(new TextLine(start, end, true));
            }
            if (newline < 0) break;
            start = newline + 1;
        }
        _lines = (text, width, wrap, lines);
        return lines;
    }

    private void WrapLine(string text, int start, int end, int width, List<TextLine> lines)
    {
        var lineStart = start;
        var columns = 0;
        var lastSpaceEnd = -1;
        var i = start;
        while (i < end)
        {
            var length = StringInfo.GetNextTextElementLength(text.AsSpan(i, end - i));
            var cluster = text.Substring(i, length);
            var clusterWidth = Width(cluster);
            if (columns + clusterWidth > width && columns > 0)
            {
                var breakAt = lastSpaceEnd > lineStart ? lastSpaceEnd : i;
                lines.Add(new TextLine(lineStart, breakAt, false));
                lineStart = breakAt;
                columns = Width(text[lineStart..i]);
                lastSpaceEnd = -1;
            }
            columns += clusterWidth;
            i += length;
            if (cluster == " ")
            {
                lastSpaceEnd = i;
            }
        }
        lines.Add(new TextLine(lineStart, end, true));
    }

    /// <summary>The caret's column and visual line. At a soft wrap the caret starts the next line.</summary>
    public (int Column, int Row) CaretPosition(int width, bool wrap)
    {
        var lines = Lines(width, wrap);
        for (var row = 0; row < lines.Count; row++)
        {
            var line = lines[row];
            if (_caret < line.End || (_caret == line.End && line.Hard))
            {
                return (Width(Text[line.Start.._caret]), row);
            }
        }
        var last = lines[^1];
        return (Width(Text[last.Start..]), lines.Count - 1);
    }

    /// <summary>The text index at a visual position, clamped to the text.</summary>
    public int IndexAt(int width, bool wrap, int column, int row)
    {
        var lines = Lines(width, wrap);
        var line = lines[Math.Clamp(row, 0, lines.Count - 1)];
        var index = line.Start;
        var columns = 0;
        foreach (var length in ElementLengths(Text[line.Start..line.End]))
        {
            var clusterWidth = Width(Text.Substring(index, length));
            if (columns + clusterWidth > column) break;
            columns += clusterWidth;
            index += length;
        }
        return index;
    }

    /// <summary>The columns a piece of the text takes, one per cluster when masked.</summary>
    public int Width(string text) => Masked ? ElementLengths(text).Count() : TextWidth.Measure(text);

    /// <summary>How a piece of the text is shown, masked or not.</summary>
    public string Display(string text) => Masked ? new string('•', Width(text)) : text;

    private bool Move(int to)
    {
        if (to == _caret) return false;
        _caret = to;
        _goalColumn = -1;
        return true;
    }

    private bool Remove(int from, int to)
    {
        if (from >= to) return false;
        _text.Remove(from, to - from);
        _caret = from;
        Changed();
        return true;
    }

    private void Changed()
    {
        _cached = null;
        _goalColumn = -1;
    }

    private string Sanitize(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        // An input's value has no line breaks, so pasted lines are joined.
        return Multiline ? text : text.Replace('\n', ' ');
    }

    private static string TakeRunes(string text, int count)
    {
        var taken = 0;
        var i = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (taken == count) break;
            i += rune.Utf16SequenceLength;
            taken++;
        }
        return text[..i];
    }

    private static IEnumerable<int> ElementLengths(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var length = StringInfo.GetNextTextElementLength(text.AsSpan(i));
            yield return length;
            i += length;
        }
    }

    private int PreviousBoundary(int position)
    {
        if (position <= 0) return 0;
        var at = 0;
        foreach (var length in ElementLengths(Text))
        {
            if (at + length >= position) return at;
            at += length;
        }
        return at;
    }

    private int NextBoundary(int position)
    {
        if (position >= _text.Length) return _text.Length;
        return position + StringInfo.GetNextTextElementLength(Text.AsSpan(position));
    }

    private static bool IsSeparator(char c) => c is ' ' or '\t' or '\n';

    private int WordStart(int position)
    {
        var i = position;
        while (i > 0 && IsSeparator(_text[i - 1])) i--;
        while (i > 0 && !IsSeparator(_text[i - 1])) i--;
        return i;
    }

    private int WordEnd(int position)
    {
        var i = position;
        while (i < _text.Length && IsSeparator(_text[i])) i++;
        while (i < _text.Length && !IsSeparator(_text[i])) i++;
        return i;
    }

    private int LineStartOf(int position)
    {
        var i = position;
        while (i > 0 && _text[i - 1] != '\n') i--;
        return i;
    }

    private int LineEndOf(int position)
    {
        var i = position;
        while (i < _text.Length && _text[i] != '\n') i++;
        return i;
    }
}
