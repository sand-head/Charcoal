using SlopTui.Layout;

namespace SlopTui.Rendering;

/// <summary>The glyphs one border glyph set draws with.</summary>
public readonly record struct BorderGlyphs(
    string TopLeft, string TopRight, string BottomLeft, string BottomRight, string Horizontal, string Vertical);

/// <summary>
/// Draws a box's border in its <c>border-color</c>, or the text colour when it
/// has none. Corners appear only where two drawn sides meet.
/// </summary>
public static class Borders
{
    public static readonly BorderGlyphs Single = new("┌", "┐", "└", "┘", "─", "│");
    public static readonly BorderGlyphs Double = new("╔", "╗", "╚", "╝", "═", "║");
    public static readonly BorderGlyphs Round = new("╭", "╮", "╰", "╯", "─", "│");
    public static readonly BorderGlyphs Bold = new("┏", "┓", "┗", "┛", "━", "┃");
    public static readonly BorderGlyphs Classic = new("+", "+", "+", "+", "-", "|");

    public static BorderGlyphs? For(BorderGlyphSet set) => set switch
    {
        BorderGlyphSet.Single => Single,
        BorderGlyphSet.Double => Double,
        BorderGlyphSet.Round => Round,
        BorderGlyphSet.Bold => Bold,
        BorderGlyphSet.Classic => Classic,
        _ => null,
    };

    public static void Draw(CellBuffer buffer, Rect rect, Style style)
    {
        if (For(style.BorderGlyphs) is not { } glyphs || rect.IsEmpty) return;

        var color = style.BorderColor.Kind == ColorKind.Default ? style.Color : style.BorderColor;
        var pen = new BorderPen(buffer, color, style.Background);
        var (left, top, right, bottom) = (rect.X, rect.Y, rect.Right - 1, rect.Bottom - 1);

        if (style.BorderTop) pen.Row(top, left, right, glyphs.Horizontal);
        if (style.BorderBottom) pen.Row(bottom, left, right, glyphs.Horizontal);
        if (style.BorderLeft) pen.Column(left, top, bottom, glyphs.Vertical);
        if (style.BorderRight) pen.Column(right, top, bottom, glyphs.Vertical);

        if (style.BorderTop && style.BorderLeft) pen.Put(left, top, glyphs.TopLeft);
        if (style.BorderTop && style.BorderRight) pen.Put(right, top, glyphs.TopRight);
        if (style.BorderBottom && style.BorderLeft) pen.Put(left, bottom, glyphs.BottomLeft);
        if (style.BorderBottom && style.BorderRight) pen.Put(right, bottom, glyphs.BottomRight);
    }

    private readonly record struct BorderPen(CellBuffer Buffer, Color Foreground, Color Background)
    {
        public void Put(int x, int y, string glyph) =>
            Buffer.Put(x, y, glyph, 1, Foreground, Background, TextStyle.None);

        public void Row(int y, int fromX, int toX, string glyph)
        {
            for (var x = fromX; x <= toX; x++) Put(x, y, glyph);
        }

        public void Column(int x, int fromY, int toY, string glyph)
        {
            for (var y = fromY; y <= toY; y++) Put(x, y, glyph);
        }
    }
}
