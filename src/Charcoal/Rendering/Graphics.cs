using System.Text;
using Charcoal.Layout;

namespace Charcoal.Rendering;

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

    /// <summary>Whether the terminal listed Sixel graphics among its primary device attributes.</summary>
    public bool Sixel { get; internal set; }

    /// <summary>Whether pictures go out as Sixel. The kitty protocol wins when both are available.</summary>
    public bool UsesSixel => Sixel && !Kitty;

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
            if (_sixelModes)
            {
                release += SixelModesOff;
            }
            return release;
        }
    }

    // Sixel pictures are not bound to cells, so each frame re-sends the ones
    // whose rows the diff repainted.

    /// <summary>
    /// Sixel scrolling leaves the cursor beside the picture (8452), so a picture
    /// on the last row does not scroll the screen, and each picture gets its own
    /// colour registers (1070).
    /// </summary>
    internal const string SixelModesOn = "\e[?8452h\e[?1070h";
    internal const string SixelModesOff = "\e[?8452l";

    private const int SixelCacheLimit = 64;

    private bool _sixelModes;
    private readonly List<SixelPlacement> _sixelFrame = [];
    private readonly HashSet<SixelPlacement> _sixelShown = new(SixelComparer.Instance);
    private readonly Dictionary<SixelPlacement, string> _sixelCache = new(SixelComparer.Instance);

    /// <summary>A picture in the frame being painted, and the part of it the clip shows.</summary>
    internal readonly record struct SixelPlacement(ImageData Image, Rect Rect, Rect Visible);

    internal void PlaceSixel(ImageData image, Rect rect, Rect visible)
    {
        lock (_pending)
        {
            _sixelFrame.Add(new SixelPlacement(image, rect, visible));
        }
    }

    internal void BeginFrame()
    {
        lock (_pending)
        {
            _sixelFrame.Clear();
        }
    }

    /// <summary>
    /// The Sixel escapes to append to a frame: every picture that is new, has
    /// moved, or stands on a row the diff repainted.
    /// </summary>
    internal string SixelOutput(bool[] rowChanged, bool everything)
    {
        lock (_pending)
        {
            if (!UsesSixel)
            {
                _sixelFrame.Clear();
                return "";
            }

            var output = new StringBuilder();
            if (!_sixelModes)
            {
                output.Append(SixelModesOn);
                _sixelModes = true;
                everything = true;
            }

            foreach (var placement in _sixelFrame)
            {
                var repaint = everything || !_sixelShown.Contains(placement) || Touches(placement.Visible, rowChanged);
                var data = EncodeSixel(placement);
                if (repaint && data.Length > 0)
                {
                    // Save and restore the cursor around the picture.
                    output.Append($"\e7\e[{placement.Visible.Y + 1};{placement.Visible.X + 1}H").Append(data).Append("\e8");
                }
            }

            _sixelShown.Clear();
            _sixelShown.UnionWith(_sixelFrame);
            _sixelFrame.Clear();
            return output.ToString();
        }
    }

    private static bool Touches(Rect visible, bool[] rowChanged)
    {
        var top = Math.Max(visible.Y, 0);
        var bottom = Math.Min(visible.Bottom, rowChanged.Length);
        for (var y = top; y < bottom; y++)
        {
            if (rowChanged[y]) return true;
        }
        return false;
    }

    private string EncodeSixel(SixelPlacement placement)
    {
        if (_sixelCache.TryGetValue(placement, out var data)) return data;

        var (rect, visible) = (placement.Rect, placement.Visible);
        var crop = new Rect(
            (visible.X - rect.X) * CellPixelWidth,
            (visible.Y - rect.Y) * CellPixelHeight,
            visible.Width * CellPixelWidth,
            visible.Height * CellPixelHeight);
        data = Rendering.Sixel.Encode(placement.Image, rect.Width * CellPixelWidth, rect.Height * CellPixelHeight, crop);

        if (_sixelCache.Count > SixelCacheLimit)
        {
            _sixelCache.Clear();
        }
        _sixelCache[placement] = data;
        return data;
    }

    private sealed class SixelComparer : IEqualityComparer<SixelPlacement>
    {
        public static readonly SixelComparer Instance = new();
        public bool Equals(SixelPlacement a, SixelPlacement b) =>
            ReferenceEquals(a.Image, b.Image) && a.Rect == b.Rect && a.Visible == b.Visible;
        public int GetHashCode(SixelPlacement key) =>
            HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(key.Image), key.Rect, key.Visible);
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
