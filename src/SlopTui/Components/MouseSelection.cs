using System.Text;
using SlopTui.Layout;
using SlopTui.Rendering;

namespace SlopTui.Components;

/// <summary>One cell of the terminal, by row and column.</summary>
public readonly record struct CellPosition(int Row, int Column) : IComparable<CellPosition>
{
    public int CompareTo(CellPosition other)
    {
        var byRow = Row.CompareTo(other.Row);
        return byRow != 0 ? byRow : Column.CompareTo(other.Column);
    }
}

/// <summary>
/// Text selected by dragging the mouse. The terminal's own selection is off
/// while it reports the mouse to the app, so the app provides one.
/// </summary>
/// <remarks>
/// A left press that no handler took anchors the selection, a drag moves its
/// other end, and the release copies the text to the clipboard with OSC 52.
/// The highlight is drawn over the painted frame and the text is read back
/// from it, so the copy is exactly what was highlighted. Positions fall between
/// cells as in a text editor: a range is half-open and a click selects nothing.
/// <c>user-select: none</c> keeps an element out of the selection, and the
/// nearest <c>user-select: contain</c> ancestor confines a drag to its box.
/// </remarks>
public sealed class MouseSelection
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether a release copies the selection. When off, the highlight stays
    /// until the next press and Ctrl+C copies it.
    /// </summary>
    public bool CopyOnRelease { get; set; } = true;

    /// <summary>Where the press landed, or null when nothing is selected.</summary>
    public CellPosition? Anchor { get; private set; }

    /// <summary>The moving end of the selection.</summary>
    public CellPosition? Focus { get; private set; }

    /// <summary>The box the drag is confined to.</summary>
    public Rect Region { get; private set; }

    /// <summary>Whether a non-empty range is selected.</summary>
    public bool Active => Normalized() is not null;

    /// <summary>Whether the button is still held after a press.</summary>
    public bool Dragging { get; private set; }

    /// <summary>Raised on the loop thread with the text that was copied.</summary>
    public event Action<string>? Copied;

    public void Press(CellPosition at, Rect region)
    {
        Anchor = at;
        Focus = at;
        Region = region;
        Dragging = true;
    }

    public void Drag(CellPosition at)
    {
        if (!Dragging) return;
        Focus = Clamp(at);
    }

    public void Release(CellPosition at)
    {
        if (!Dragging) return;
        Focus = Clamp(at);
        Dragging = false;
    }

    private CellPosition Clamp(CellPosition at)
    {
        var lastRow = Math.Max(Region.Y, Region.Bottom - 1);
        return new CellPosition(
            Math.Clamp(at.Row, Region.Y, lastRow),
            Math.Clamp(at.Column, Region.X, Region.Right));
    }

    public void Clear()
    {
        Anchor = null;
        Focus = null;
        Dragging = false;
    }

    /// <summary>The selected range with the start first, whichever way the drag went.</summary>
    public (CellPosition Start, CellPosition End)? Normalized()
    {
        if (Anchor is not { } anchor || Focus is not { } focus) return null;

        var order = anchor.CompareTo(focus);
        if (order == 0) return null;
        return order < 0 ? (anchor, focus) : (focus, anchor);
    }

    public bool Contains(int row, int column)
    {
        if (Normalized() is not { } range) return false;
        if (row < range.Start.Row || row > range.End.Row) return false;
        if (column < Region.X || column >= Region.Right) return false;
        if (row == range.Start.Row && column < range.Start.Column) return false;
        if (row == range.End.Row && column >= range.End.Column) return false;
        return true;
    }

    /// <summary>Highlights the selected cells of a painted frame in inverse video.</summary>
    public void Highlight(CellBuffer buffer)
    {
        if (Normalized() is not { } range) return;
        for (var row = Math.Max(0, range.Start.Row); row <= range.End.Row && row < buffer.Height; row++)
        {
            for (var column = Math.Max(0, Region.X); column < Math.Min(buffer.Width, Region.Right); column++)
            {
                if (!Contains(row, column)) continue;
                var cell = buffer[column, row];
                buffer[column, row] = cell with { Style = cell.Style ^ TextStyle.Inverse };
            }
        }
    }

    /// <summary>
    /// The selected text of a painted frame, one line per row without trailing
    /// blanks, or null when only blanks are selected. A wide glyph is copied
    /// when either of its halves is selected.
    /// </summary>
    public string? Text(CellBuffer buffer)
    {
        if (Normalized() is not { } range) return null;

        var lines = new List<string>();
        for (var row = Math.Max(0, range.Start.Row); row <= range.End.Row && row < buffer.Height; row++)
        {
            lines.Add(RowText(buffer, row).TrimEnd());
        }
        var text = string.Join("\n", lines);
        return text.Trim().Length == 0 ? null : text;
    }

    private string RowText(CellBuffer buffer, int row)
    {
        var line = new StringBuilder();
        for (var column = Math.Max(0, Region.X); column < Math.Min(buffer.Width, Region.Right); column++)
        {
            if (!Contains(row, column)) continue;

            var cell = buffer[column, row];
            if (!cell.IsContinuation)
            {
                line.Append(cell.Cluster);
            }
            else if (column > Region.X && !Contains(row, column - 1))
            {
                // Only the right half of a wide glyph is selected.
                line.Append(buffer[column - 1, row].Cluster);
            }
        }
        return line.ToString();
    }

    internal void RaiseCopied(string text) => Copied?.Invoke(text);

    /// <summary>
    /// Whether a press on <paramref name="element"/> may start a selection, and
    /// the box the drag is confined to. The nearest <c>none</c>, <c>text</c> or
    /// <c>all</c> among the element and its ancestors decides.
    /// </summary>
    internal static bool Allows(HostElement element, Rect screen, out Rect region)
    {
        region = screen;
        UserSelect? decision = null;
        for (var current = element; current is not null; current = current.Parent?.ClosestElement)
        {
            var select = current.Node.Style.UserSelect;
            if (decision is null && select is UserSelect.None or UserSelect.Text or UserSelect.All)
            {
                decision = select;
            }
            if (select == UserSelect.Contain && decision != UserSelect.None)
            {
                region = current.Node.Layout.Deflate(current.Node.Style.BorderEdges);
                return true;
            }
        }
        return decision != UserSelect.None;
    }
}
