namespace Charcoal.Layout;

/// <summary>A width and a height, in cells.</summary>
public readonly record struct Size(int Width, int Height)
{
    public static readonly Size Empty = new(0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>A rectangle of cells; <see cref="X"/> and <see cref="Y"/> are the top-left corner.</summary>
public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    public static readonly Rect Empty = new(0, 0, 0, 0);

    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public Size Size => new(Width, Height);

    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;

    /// <summary>The overlap of two rectangles, or <see cref="Empty"/> when they do not meet.</summary>
    public Rect Intersect(Rect other)
    {
        var x = Math.Max(X, other.X);
        var y = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right > x && bottom > y ? new Rect(x, y, right - x, bottom - y) : Empty;
    }

    /// <summary>This rectangle shrunk by the given edges; never below zero size.</summary>
    public Rect Deflate(Edges edges) => new(
        X + edges.Left,
        Y + edges.Top,
        Math.Max(0, Width - edges.Left - edges.Right),
        Math.Max(0, Height - edges.Top - edges.Bottom));

    public Rect Offset(int dx, int dy) => new(X + dx, Y + dy, Width, Height);
}

/// <summary>Space on four sides, in cells.</summary>
public readonly record struct Edges(int Top, int Right, int Bottom, int Left)
{
    public static readonly Edges Zero = new(0, 0, 0, 0);

    public Edges(int all) : this(all, all, all, all) { }
    public Edges(int vertical, int horizontal) : this(vertical, horizontal, vertical, horizontal) { }

    public int Horizontal => Left + Right;
    public int Vertical => Top + Bottom;

    public static Edges operator +(Edges a, Edges b) =>
        new(a.Top + b.Top, a.Right + b.Right, a.Bottom + b.Bottom, a.Left + b.Left);

    /// <summary>The CSS shorthand, which <c>StyleParser</c> reads back.</summary>
    public override string ToString()
    {
        if (Top != Bottom || Left != Right) return $"{Top} {Right} {Bottom} {Left}";
        if (Top != Left) return $"{Top} {Left}";
        return Top.ToString();
    }
}
