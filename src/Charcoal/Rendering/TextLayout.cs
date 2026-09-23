using System.Globalization;
using System.Text;
using Charcoal.Layout;

namespace Charcoal.Rendering;

/// <summary>
/// Wraps, truncates or clips styled runs to a width in terminal columns,
/// never splitting a grapheme cluster.
/// </summary>
public static class TextLayout
{
    public const string Ellipsis = "…";

    /// <summary>
    /// Applies CSS white-space processing to the runs in place. Under
    /// <c>normal</c> and <c>nowrap</c> each run of whitespace becomes one
    /// space and spaces at line edges go; <c>pre-line</c> also keeps
    /// newlines; <c>pre</c> and <c>pre-wrap</c> keep everything. Forced
    /// breaks are never whitespace.
    /// </summary>
    public static void CollapseWhitespace(List<TextRun> runs, WhiteSpace mode)
    {
        if (mode is WhiteSpace.Pre or WhiteSpace.PreWrap) return;

        var collapser = new WhitespaceCollapser(keepNewlines: mode == WhiteSpace.PreLine);
        foreach (var run in runs)
        {
            collapser.Add(run);
        }
        var collapsed = collapser.Finish();
        runs.Clear();
        runs.AddRange(collapsed.Where(run => run.Text.Length > 0));
    }

    private sealed class WhitespaceCollapser(bool keepNewlines)
    {
        private readonly List<TextRun> _result = [];
        private readonly StringBuilder _text = new();
        private bool _atLineStart = true;
        private bool _trailingSpace;

        public void Add(TextRun run)
        {
            if (run.Break)
            {
                EndLine();
                _result.Add(run);
                return;
            }

            foreach (var c in run.Text)
            {
                AddCharacter(c);
            }
            if (_text.Length > 0)
            {
                _result.Add(run with { Text = _text.ToString() });
                _text.Clear();
            }
        }

        public List<TextRun> Finish()
        {
            TrimTrailingSpace();
            return _result;
        }

        private void AddCharacter(char c)
        {
            if (c == '\n' && keepNewlines)
            {
                EndLine();
                _text.Append('\n');
            }
            else if (c is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                if (_atLineStart || _trailingSpace) return;
                _text.Append(' ');
                _trailingSpace = true;
            }
            else
            {
                _text.Append(c);
                _atLineStart = false;
                _trailingSpace = false;
            }
        }

        private void EndLine()
        {
            TrimTrailingSpace();
            _atLineStart = true;
        }

        /// <summary>Drops the space before a line's end, even when an earlier run holds it.</summary>
        private void TrimTrailingSpace()
        {
            if (!_trailingSpace) return;
            _trailingSpace = false;
            if (_text.Length > 0)
            {
                _text.Length--;
                return;
            }

            for (var i = _result.Count - 1; i >= 0; i--)
            {
                if (_result[i].Break) return;
                var text = _result[i].Text;
                if (text.Length == 0)
                {
                    _result.RemoveAt(i);
                    continue;
                }

                var trimmed = text[..^1];
                if (trimmed.Length == 0)
                {
                    _result.RemoveAt(i);
                }
                else
                {
                    _result[i] = _result[i] with { Text = trimmed };
                }
                return;
            }
        }
    }

    /// <summary>The visual lines of the runs at a width. A <c>"\n"</c> always breaks the line.</summary>
    public static List<List<TextRun>> Wrap(IReadOnlyList<TextRun> runs, int width, TextWrap mode)
    {
        var result = new List<List<TextRun>>();
        if (runs.Count == 0 || runs.All(r => r.Text.Length == 0)) return result;

        foreach (var line in SplitLines(runs))
        {
            switch (mode)
            {
                case TextWrap.Wrap:
                    result.AddRange(WrapLine(line, width));
                    break;
                case TextWrap.Clip:
                    result.Add(line);
                    break;
                default:
                    result.Add(TruncateLine(line, width, mode));
                    break;
            }
        }

        return result;
    }

    /// <summary>The widest line and the line count, never wider than the available width.</summary>
    public static Size Measure(IReadOnlyList<TextRun> runs, int? availableWidth, TextWrap mode)
    {
        var lines = Wrap(runs, availableWidth ?? int.MaxValue, mode);
        var widest = 0;
        foreach (var line in lines) widest = Math.Max(widest, LineWidth(line));
        if (availableWidth is { } limit) widest = Math.Min(widest, Math.Max(0, limit));
        return new Size(widest, lines.Count);
    }

    /// <summary>
    /// The narrowest width that loses nothing: the widest word when wrapping,
    /// otherwise the widest line.
    /// </summary>
    public static int MinContentWidth(IReadOnlyList<TextRun> runs, TextWrap mode)
    {
        if (mode != TextWrap.Wrap) return Measure(runs, null, mode).Width;

        var widest = 0;
        var word = 0;
        foreach (var run in runs)
        {
            foreach (var (cluster, width) in TextWidth.Clusters(run.Text))
            {
                if (cluster is " " or "\n")
                {
                    widest = Math.Max(widest, word);
                    word = 0;
                }
                else
                {
                    word += Math.Max(0, width);
                }
            }
        }
        return Math.Max(widest, word);
    }

    public static int LineWidth(IReadOnlyList<TextRun> line)
    {
        var width = 0;
        foreach (var run in line) width += TextWidth.Measure(run.Text);
        return width;
    }

    private static List<List<TextRun>> SplitLines(IReadOnlyList<TextRun> runs)
    {
        var lines = new List<List<TextRun>> { new() };
        foreach (var run in runs)
        {
            if (run.Break)
            {
                lines.Add([]);
                continue;
            }
            var text = run.Text;
            var start = 0;
            while (true)
            {
                var newline = text.IndexOf('\n', start);
                if (newline < 0)
                {
                    if (start < text.Length) lines[^1].Add(run with { Text = text[start..] });
                    break;
                }
                if (newline > start) lines[^1].Add(run with { Text = text[start..newline] });
                lines.Add([]);
                start = newline + 1;
            }
        }
        return lines;
    }

    /// <summary>Wraps one source line, breaking at spaces where possible.</summary>
    private static List<List<TextRun>> WrapLine(List<TextRun> line, int width)
    {
        if (width <= 0) return [line];

        var rows = new List<List<TextRun>>();
        var current = new List<TextRun>();
        var used = 0;

        void EndRow()
        {
            TrimTrailingSpaces(current);
            rows.Add(current);
            current = [];
            used = 0;
        }

        foreach (var run in line)
        {
            var text = run.Text;
            while (text.Length > 0)
            {
                var room = width - used;
                if (room <= 0)
                {
                    EndRow();
                    text = text.TrimStart(' ');
                    continue;
                }

                var taken = TextWidth.TakePrefix(text, room, out var columns);
                if (taken == text.Length)
                {
                    current.Add(run with { Text = text });
                    used += columns;
                    break;
                }

                if (taken == 0)
                {
                    // A glyph wider than an empty row can never fit, so drop it.
                    if (used > 0)
                    {
                        EndRow();
                        continue;
                    }
                    text = text[StringInfo.GetNextTextElementLength(text)..];
                    continue;
                }

                // Without a space to break at, a word moves to the next row, and
                // is only split when it is wider than a whole row.
                var cut = text[taken] == ' ' ? taken : text.LastIndexOf(' ', taken - 1);
                if (cut <= 0)
                {
                    if (used > 0)
                    {
                        EndRow();
                        text = text.TrimStart(' ');
                        continue;
                    }
                    cut = taken;
                }

                current.Add(run with { Text = text[..cut] });
                EndRow();
                text = text[cut..].TrimStart(' ');
            }
        }

        rows.Add(current);
        return rows;
    }

    private static void TrimTrailingSpaces(List<TextRun> row)
    {
        while (row.Count > 0)
        {
            var trimmed = row[^1].Text.TrimEnd(' ');
            if (trimmed.Length > 0)
            {
                row[^1] = row[^1] with { Text = trimmed };
                return;
            }
            row.RemoveAt(row.Count - 1);
        }
    }

    /// <summary>Cuts one line to the width, with an ellipsis at the end, start or middle.</summary>
    private static List<TextRun> TruncateLine(List<TextRun> line, int width, TextWrap mode)
    {
        if (LineWidth(line) <= width) return line;
        if (width <= 0) return [];

        var keep = width - 1;
        switch (mode)
        {
            case TextWrap.TruncateStart:
            {
                var tail = Suffix(line, keep, out var styleOfCut);
                tail.Insert(0, styleOfCut with { Text = Ellipsis });
                return Merge(tail);
            }
            case TextWrap.TruncateMiddle:
            {
                var headColumns = (keep + 1) / 2;
                var head = Prefix(line, headColumns, out _, out var styleOfCut);
                var tail = Suffix(line, keep - headColumns, out _);
                head.Add(styleOfCut with { Text = Ellipsis });
                head.AddRange(tail);
                return Merge(head);
            }
            default:
            {
                var head = Prefix(line, keep, out _, out var styleOfCut);
                head.Add(styleOfCut with { Text = Ellipsis });
                return Merge(head);
            }
        }
    }

    /// <summary>The first columns of a line, and the run the cut fell in.</summary>
    private static List<TextRun> Prefix(List<TextRun> line, int columns, out int used, out TextRun cutRun)
    {
        var result = new List<TextRun>();
        used = 0;
        cutRun = line.Count > 0 ? line[0] with { Text = "" } : new TextRun("");
        foreach (var run in line)
        {
            var taken = TextWidth.TakePrefix(run.Text, columns - used, out var runColumns);
            if (taken > 0) result.Add(run with { Text = run.Text[..taken] });
            used += runColumns;
            cutRun = run with { Text = "" };
            if (taken < run.Text.Length) break;
        }
        return result;
    }

    /// <summary>The last columns of a line, and the run the cut fell in.</summary>
    private static List<TextRun> Suffix(List<TextRun> line, int columns, out TextRun cutRun)
    {
        var clusters = new List<(string Cluster, int Width, int Run)>();
        for (var i = 0; i < line.Count; i++)
        {
            foreach (var (cluster, width) in TextWidth.Clusters(line[i].Text))
            {
                clusters.Add((cluster, width, i));
            }
        }

        var used = 0;
        var start = clusters.Count;
        while (start > 0 && used + clusters[start - 1].Width <= columns)
        {
            start--;
            used += clusters[start].Width;
        }

        var runOfCut = start > 0 ? clusters[start - 1].Run : 0;
        cutRun = line.Count > 0 ? line[runOfCut] with { Text = "" } : new TextRun("");

        var result = new List<TextRun>();
        for (var i = start; i < clusters.Count; i++)
        {
            AppendMerged(result, line[clusters[i].Run] with { Text = clusters[i].Cluster });
        }
        return result;
    }

    /// <summary>Joins adjacent runs that look the same.</summary>
    private static List<TextRun> Merge(List<TextRun> runs)
    {
        var result = new List<TextRun>();
        foreach (var run in runs)
        {
            if (run.Text.Length > 0) AppendMerged(result, run);
        }
        return result;
    }

    private static void AppendMerged(List<TextRun> runs, TextRun run)
    {
        if (runs.Count > 0 && LooksLike(runs[^1], run))
        {
            runs[^1] = runs[^1] with { Text = runs[^1].Text + run.Text };
        }
        else
        {
            runs.Add(run);
        }
    }

    private static bool LooksLike(TextRun a, TextRun b) =>
        a.Foreground == b.Foreground && a.Background == b.Background && a.Style == b.Style;
}
