using System.Diagnostics.CodeAnalysis;
using System.Text;
using Charcoal.Layout;

namespace Charcoal.Rendering;

/// <summary>
/// One terminal cell. A wide cluster owns the cells to its right, which hold
/// continuations of <see cref="Width"/> 0.
/// </summary>
public readonly record struct Cell(string Cluster, byte Width, Color Foreground, Color Background, TextStyle Style)
{
    public static readonly Cell Blank = new(" ", 1, Color.Default, Color.Default, TextStyle.None);
    public static readonly Cell Continuation = new("", 0, Color.Default, Color.Default, TextStyle.None);

    public bool IsContinuation => Width == 0;

    public static Cell Space(Color foreground, Color background) => new(" ", 1, foreground, background, TextStyle.None);
}

/// <summary>
/// A grid of cells in absolute terminal coordinates. Writes outside the clip
/// are dropped, and overwriting half of a wide cluster blanks the other half.
/// </summary>
public sealed class CellBuffer
{
    private Cell[] _cells;
    private long[] _rowHashes;
    private bool[] _rowHashValid;
    private readonly Stack<Rect> _clips = new();

    public CellBuffer(int width, int height) => Resize(width, height);

    public int Width { get; private set; }
    public int Height { get; private set; }

    public Rect Clip { get; private set; }

    public Cell this[int x, int y]
    {
        get => _cells[y * Width + x];
        set
        {
            _cells[y * Width + x] = value;
            _rowHashValid[y] = false;
        }
    }

    public void PushClip(Rect rect)
    {
        _clips.Push(Clip);
        Clip = Clip.Intersect(rect);
    }

    public void PopClip()
    {
        Clip = _clips.Count > 0 ? _clips.Pop() : new Rect(0, 0, Width, Height);
    }

    /// <summary>Resizes the buffer and blanks every cell.</summary>
    [MemberNotNull(nameof(_cells), nameof(_rowHashes), nameof(_rowHashValid))]
    public void Resize(int width, int height)
    {
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        _cells = new Cell[Width * Height];
        Array.Fill(_cells, Cell.Blank);
        _rowHashes = new long[Height];
        _rowHashValid = new bool[Height];
        _clips.Clear();
        Clip = new Rect(0, 0, Width, Height);
    }

    /// <summary>Fills the whole buffer, ignoring the clip.</summary>
    public void Fill(Cell cell)
    {
        Array.Fill(_cells, cell);
        Array.Fill(_rowHashValid, false);
    }

    public void Fill(Rect rect, Cell cell)
    {
        var r = rect.Intersect(Clip);
        if (r.IsEmpty) return;
        for (var y = r.Y; y < r.Bottom; y++)
        {
            for (var x = r.X; x < r.Right; x++) PutCell(x, y, cell);
            _rowHashValid[y] = false;
        }
    }

    /// <summary>Writes one cluster, or nothing if any of its columns fall outside the clip.</summary>
    /// <returns>The columns written.</returns>
    public int Put(int x, int y, string cluster, int width, Color foreground, Color background, TextStyle style)
    {
        if (width <= 0) return 0;
        if (y < Clip.Y || y >= Clip.Bottom || x < Clip.X || x + width > Clip.Right) return 0;

        // Without a background of its own, text sits on whatever is already there.
        if (background.Kind == ColorKind.Default) background = _cells[y * Width + x].Background;
        PutCell(x, y, new Cell(cluster, (byte)width, foreground, background, style));
        for (var i = 1; i < width; i++)
        {
            _cells[y * Width + x + i] = Cell.Continuation with { Foreground = foreground, Background = background, Style = style };
        }
        _rowHashValid[y] = false;
        return width;
    }

    /// <summary>
    /// Writes text along one row. Clusters left of the clip take up their
    /// columns without being drawn, and the first cluster past its right edge
    /// ends the write.
    /// </summary>
    /// <returns>The columns used, drawn or not.</returns>
    public int PutText(int x, int y, string text, Color foreground, Color background, TextStyle style)
    {
        var used = 0;
        foreach (var (cluster, width) in TextWidth.Clusters(text))
        {
            if (width == 0) continue;
            var at = x + used;
            if (at < Clip.X)
            {
                used += width;
                continue;
            }
            if (Put(at, y, cluster, width, foreground, background, style) == 0) break;
            used += width;
        }
        return used;
    }

    private void PutCell(int x, int y, Cell cell)
    {
        var index = y * Width + x;
        var existing = _cells[index];
        if (existing.IsContinuation && x > 0)
        {
            BlankWideOwner(index - 1);
        }
        else if (existing.Width > 1)
        {
            BlankContinuations(index, x, existing);
        }
        _cells[index] = cell;
    }

    private void BlankWideOwner(int index)
    {
        var owner = _cells[index];
        if (owner.Width > 1)
        {
            _cells[index] = Cell.Space(owner.Foreground, owner.Background);
        }
    }

    private void BlankContinuations(int index, int x, Cell owner)
    {
        for (var i = 1; i < owner.Width && x + i < Width; i++)
        {
            if (_cells[index + i].IsContinuation)
            {
                _cells[index + i] = Cell.Space(owner.Foreground, owner.Background);
            }
        }
    }

    /// <summary>A hash of one row's cells, cached until the row is written.</summary>
    public long RowHash(int y)
    {
        if (_rowHashValid[y]) return _rowHashes[y];
        var hash = new HashCode();
        var start = y * Width;
        for (var i = 0; i < Width; i++) hash.Add(_cells[start + i]);
        _rowHashes[y] = hash.ToHashCode();
        _rowHashValid[y] = true;
        return _rowHashes[y];
    }

    public void CopyFrom(CellBuffer other)
    {
        if (other.Width != Width || other.Height != Height)
            throw new ArgumentException("Buffers differ in size.", nameof(other));
        Array.Copy(other._cells, _cells, _cells.Length);
        Array.Copy(other._rowHashes, _rowHashes, _rowHashes.Length);
        Array.Copy(other._rowHashValid, _rowHashValid, _rowHashValid.Length);
    }

    /// <summary>The row as plain text.</summary>
    public string RowText(int y)
    {
        var chars = new StringBuilder(Width);
        for (var x = 0; x < Width; x++)
        {
            var cell = _cells[y * Width + x];
            if (!cell.IsContinuation) chars.Append(cell.Cluster);
        }
        return chars.ToString();
    }

    /// <summary>All rows as text, each with its trailing blanks trimmed.</summary>
    public override string ToString() =>
        string.Join('\n', Enumerable.Range(0, Height).Select(y => RowText(y).TrimEnd()));
}
