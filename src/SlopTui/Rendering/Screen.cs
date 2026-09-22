using System.Text;

namespace SlopTui.Rendering;

/// <summary>
/// Double-buffers the terminal: a frame paints into <see cref="Back"/>, and
/// <see cref="Flush"/> returns the escape codes that bring the terminal from
/// what it shows to that.
/// </summary>
public sealed class Screen
{
    // No cluster is 255 columns wide, so this never equals a painted cell.
    private static readonly Cell Unknown = new("￿", 255, Color.Default, Color.Default, TextStyle.None);

    private readonly StringBuilder _output = new();
    private CellBuffer _shown;
    private bool[] _rowChanged;
    private (Color Fg, Color Bg, TextStyle Style)? _pen;
    private (int X, int Y)? _shownCursor;
    private bool _shownCursorVisible;

    public Screen(int width, int height)
    {
        _shown = new CellBuffer(width, height);
        _shown.Fill(Unknown);
        Back = new CellBuffer(width, height);
        _rowChanged = new bool[height];
    }

    public CellBuffer Back { get; private set; }

    public int Width => Back.Width;
    public int Height => Back.Height;

    /// <summary>Wrap each frame in DEC mode 2026 so the terminal shows it whole.</summary>
    public bool SynchronizedOutput { get; set; }

    /// <summary>Where the cursor should rest after the frame, in absolute cells.</summary>
    public (int X, int Y)? Cursor { get; set; }

    public bool CursorVisible { get; set; }

    /// <summary>Resizes both buffers; the next frame repaints in full.</summary>
    public void Resize(int width, int height)
    {
        Back = new CellBuffer(width, height);
        _shown = new CellBuffer(width, height);
        _rowChanged = new bool[height];
        Invalidate();
    }

    /// <summary>Makes the next frame repaint in full.</summary>
    public void Invalidate()
    {
        _shown.Fill(Unknown);
        _shownCursor = null;
        _shownCursorVisible = false;
        _pen = null;
    }

    /// <summary>The escape codes for everything that changed, or an empty string.</summary>
    public string Flush() => Flush(null);

    /// <summary>
    /// As <see cref="Flush()"/>, appending the output of <paramref name="trailer"/>,
    /// which is told the rows this frame repainted, before the cursor is placed.
    /// </summary>
    public string Flush(Func<bool[], string>? trailer)
    {
        _output.Clear();
        _pen = null;
        Array.Fill(_rowChanged, false);

        var body = Paint();
        var trailing = trailer?.Invoke(_rowChanged) ?? "";
        var drawing = body.Length > 0 || trailing.Length > 0;
        var cursorChanged = Cursor != _shownCursor || CursorVisible != _shownCursorVisible;
        if (!drawing && !cursorChanged) return "";

        var frame = new StringBuilder();
        if (SynchronizedOutput) frame.Append("\e[?2026h");
        if (drawing)
        {
            frame.Append("\e[?25l");
        }
        if (body.Length > 0)
        {
            frame.Append(body);
            frame.Append("\e[0m");
        }
        frame.Append(trailing);
        if (CursorVisible && Cursor is { } cursor)
        {
            frame.Append(Position(cursor.X, cursor.Y));
            frame.Append("\e[?25h");
        }
        else if (!drawing)
        {
            frame.Append("\e[?25l");
        }
        if (SynchronizedOutput) frame.Append("\e[?2026l");

        _shown.CopyFrom(Back);
        _shownCursor = Cursor;
        _shownCursorVisible = CursorVisible;
        return frame.ToString();
    }

    private string Paint()
    {
        for (var y = 0; y < Back.Height; y++)
        {
            if (_shown.RowHash(y) == Back.RowHash(y)) continue;
            _rowChanged[y] = true;

            var (first, last) = Bounds(y);
            if (first < 0) continue;

            var content = LastContent(y);
            var paintTo = Math.Min(last, content);

            _output.Append(Position(first, y));
            for (var x = first; x <= paintTo; x++)
            {
                var cell = Back[x, y];
                if (cell.IsContinuation) continue;
                Pen(cell.Foreground, cell.Background, cell.Style);
                _output.Append(cell.Cluster);
            }

            if (content < last)
            {
                if (paintTo < first) _output.Append(Position(first, y));
                Pen(Color.Default, Color.Default, TextStyle.None);
                _output.Append("\e[K");
            }
        }

        return _output.ToString();
    }

    /// <summary>The first and last columns of a row that differ, or (-1, -1).</summary>
    private (int First, int Last) Bounds(int y)
    {
        var first = -1;
        var last = -1;
        for (var x = 0; x < Back.Width; x++)
        {
            if (_shown[x, y] == Back[x, y]) continue;
            if (first < 0) first = x;
            last = x;
        }

        while (first > 0 && Back[first, y].IsContinuation) first--;
        return (first, last);
    }

    /// <summary>The last column of a row holding anything other than a plain blank.</summary>
    private int LastContent(int y)
    {
        for (var x = Back.Width - 1; x >= 0; x--)
        {
            if (Back[x, y] != Cell.Blank) return x;
        }
        return -1;
    }

    /// <summary>
    /// Switches colours and flags. Dropping a flag needs a full reset, since
    /// not every terminal honours the SGR codes that turn one flag off.
    /// </summary>
    private void Pen(Color fg, Color bg, TextStyle style)
    {
        var key = (fg, bg, style);
        if (_pen == key) return;

        var previous = _pen;
        _pen = key;

        _output.Append("\e[");
        var dropping = previous is null || (previous.Value.Style & ~style) != 0;
        if (dropping)
        {
            _output.Append("0;").Append(fg.ToSgr(true)).Append(';').Append(bg.ToSgr(false));
            AppendStyleCodes(style, TextStyle.None);
        }
        else
        {
            var parts = new List<string>();
            if (previous!.Value.Fg != fg) parts.Add(fg.ToSgr(true));
            if (previous.Value.Bg != bg) parts.Add(bg.ToSgr(false));
            _output.Append(string.Join(';', parts));
            AppendStyleCodes(style, previous.Value.Style, separatorFirst: parts.Count > 0);
        }
        _output.Append('m');
    }

    private void AppendStyleCodes(TextStyle style, TextStyle already, bool separatorFirst = true)
    {
        var added = style & ~already;
        foreach (var (flag, code) in StyleCodes)
        {
            if ((added & flag) == 0) continue;
            if (separatorFirst) _output.Append(';');
            separatorFirst = true;
            _output.Append(code);
        }
    }

    private static readonly (TextStyle Flag, string Code)[] StyleCodes =
    [
        (TextStyle.Bold, "1"),
        (TextStyle.Dim, "2"),
        (TextStyle.Italic, "3"),
        (TextStyle.Underline, "4"),
        (TextStyle.Inverse, "7"),
        (TextStyle.Strikethrough, "9"),
    ];

    private static string Position(int x, int y) => $"\e[{y + 1};{x + 1}H";
}
