using System.Text;
using SlopTui.Layout;

namespace SlopTui.Rendering;

/// <summary>
/// What the terminal can do with pictures, and the images sent to it. Until
/// the terminal answers the capability queries, pictures are half blocks.
/// </summary>
public sealed class Graphics
{
    private readonly Dictionary<(ImageData Image, int Cols, int Rows), int> _placements = new(PlacementComparer.Instance);
    private readonly StringBuilder _pending = new();
    private readonly int _idBase;
    private int _next;

    public Graphics()
    {
        // Image ids are shared by every program using the terminal, so a
        // per-process high byte keeps apps from replacing each other's images.
        _idBase = ((Environment.ProcessId & 0x7F) + 1) << 16;
    }

    /// <summary>Whether the terminal answered the kitty graphics query with OK.</summary>
    public bool Kitty { get; internal set; }

    /// <summary>Whether the capability queries have been answered or known to go unanswered.</summary>
    public bool Detected { get; internal set; }

    /// <summary>The cell size in pixels, if the terminal reported it.</summary>
    public Size? CellPixels { get; internal set; }

    public int CellPixelWidth => CellPixels is { Width: > 0 } size ? size.Width : ImagePainter.CellPixelWidth;

    public int CellPixelHeight => CellPixels is { Height: > 0 } size ? size.Height : ImagePainter.CellPixelHeight;

    /// <summary>Whether pictures are sent as protocol images rather than half blocks.</summary>
    public bool UsesProtocol => Kitty;

    /// <summary>Whether image transmissions are waiting to be written.</summary>
    internal bool HasPending => _pending.Length > 0;

    /// <summary>
    /// The id of the image placed over <paramref name="cols"/> × <paramref name="rows"/>
    /// cells, queueing its transmission the first time. Each size is a separate image.
    /// </summary>
    public int Place(ImageData image, int cols, int rows)
    {
        cols = Math.Clamp(cols, 1, KittyGraphics.MaxSpan);
        rows = Math.Clamp(rows, 1, KittyGraphics.MaxSpan);
        var key = (image, cols, rows);
        lock (_pending)
        {
            if (_placements.TryGetValue(key, out var id)) return id;
            id = _idBase + (++_next & 0xFFFF);
            _placements[key] = id;
            _pending.Append(KittyGraphics.Transmit(image, id, cols, rows));
            return id;
        }
    }

    /// <summary>Takes the queued transmissions, to be written ahead of the frame that uses them.</summary>
    internal string TakePending()
    {
        lock (_pending)
        {
            if (_pending.Length == 0) return "";
            var text = _pending.ToString();
            _pending.Clear();
            return text;
        }
    }

    /// <summary>The escapes that free every image this app sent.</summary>
    internal string ReleaseAll()
    {
        lock (_pending)
        {
            var release = string.Concat(_placements.Values.Select(KittyGraphics.Delete));
            _placements.Clear();
            _pending.Clear();
            return release;
        }
    }

    /// <summary>Compares images by identity, so the same pixels loaded twice are two images.</summary>
    private sealed class PlacementComparer : IEqualityComparer<(ImageData Image, int Cols, int Rows)>
    {
        public static readonly PlacementComparer Instance = new();
        public bool Equals((ImageData Image, int Cols, int Rows) a, (ImageData Image, int Cols, int Rows) b) =>
            ReferenceEquals(a.Image, b.Image) && a.Cols == b.Cols && a.Rows == b.Rows;
        public int GetHashCode((ImageData Image, int Cols, int Rows) key) =>
            HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(key.Image), key.Cols, key.Rows);
    }
}
