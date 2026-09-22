using SlopTui.Layout;

namespace SlopTui.Rendering;

/// <summary>
/// Paints an image as half blocks, two pixel rows per cell. Sizes assume a
/// cell of about 8×16 pixels, so a picture keeps its shape.
/// </summary>
public static class ImagePainter
{
    public const int CellPixelWidth = 8;
    public const int CellPixelHeight = 16;

    /// <summary>
    /// The size in cells. Given both sides, they are used as they are; given
    /// one, the other follows the aspect ratio; given neither, the image is at
    /// its natural size, shrunk to the available width.
    /// </summary>
    public static Size Fit(ImageData image, int? width, int? height, int? availableWidth) =>
        Fit(image, width, height, availableWidth, CellPixelWidth, CellPixelHeight);

    /// <summary>As <see cref="Fit(ImageData, int?, int?, int?)"/>, with the terminal's real cell size.</summary>
    public static Size Fit(ImageData image, int? width, int? height, int? availableWidth, int cellWidth, int cellHeight)
    {
        var ratio = (double)image.Height / image.Width;
        switch (width, height)
        {
            case ({ } givenWidth, { } givenHeight):
                return new Size(Math.Max(1, givenWidth), Math.Max(1, givenHeight));
            case ({ } onlyWidth, null):
                return new Size(Math.Max(1, onlyWidth), Rows(onlyWidth, ratio, cellWidth, cellHeight));
            case (null, { } onlyHeight):
                return new Size(Columns(onlyHeight, ratio, cellWidth, cellHeight), Math.Max(1, onlyHeight));
        }

        var naturalColumns = (int)Math.Ceiling(image.Width / (double)cellWidth);
        if (availableWidth is { } available && available < naturalColumns)
        {
            var columns = Math.Max(1, available);
            return new Size(columns, Rows(columns, ratio, cellWidth, cellHeight));
        }
        return new Size(naturalColumns, (int)Math.Ceiling(image.Height / (double)cellHeight));
    }

    private static int Rows(int columns, double ratio, int cellWidth, int cellHeight) =>
        Math.Max(1, (int)Math.Round(columns * ratio * cellWidth / cellHeight));

    private static int Columns(int rows, double ratio, int cellWidth, int cellHeight) =>
        Math.Max(1, (int)Math.Round(rows / ratio * cellHeight / cellWidth));

    /// <summary>
    /// Paints the image with the terminal's graphics protocol when it has one,
    /// and as half blocks otherwise.
    /// </summary>
    public static void Paint(CellBuffer buffer, Rect rect, ImageData image, Graphics? graphics)
    {
        if (rect.IsEmpty) return;
        if (graphics is { UsesProtocol: true })
        {
            var id = graphics.Place(image, rect.Width, rect.Height);
            KittyGraphics.PaintPlaceholders(buffer, rect, id);
            return;
        }
        if (graphics is { UsesSixel: true })
        {
            // Blank the covered cells so the diff clears what was under the
            // picture, which goes out after the diff.
            var visible = rect.Intersect(buffer.Clip);
            if (visible.IsEmpty) return;
            buffer.Fill(visible, Cell.Blank);
            graphics.PlaceSixel(image, rect, visible);
            return;
        }
        Paint(buffer, rect, image);
    }

    /// <summary>Paints the image stretched over <paramref name="rect"/>.</summary>
    public static void Paint(CellBuffer buffer, Rect rect, ImageData image)
    {
        if (rect.IsEmpty) return;
        var columns = rect.Width;
        var pixelRows = rect.Height * 2;
        for (var row = 0; row < rect.Height; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var top = Sample(image, column, row * 2, columns, pixelRows);
                var bottom = Sample(image, column, row * 2 + 1, columns, pixelRows);
                PaintCell(buffer, rect.X + column, rect.Y + row, top, bottom);
            }
        }
    }

    /// <summary>Draws the opaque halves of a cell and leaves a transparent cell alone.</summary>
    private static void PaintCell(CellBuffer buffer, int x, int y, (Color Color, bool Opaque) top, (Color Color, bool Opaque) bottom)
    {
        if (top.Opaque)
        {
            var background = bottom.Opaque ? bottom.Color : Color.Default;
            buffer.Put(x, y, "▀", 1, top.Color, background, TextStyle.None);
        }
        else if (bottom.Opaque)
        {
            buffer.Put(x, y, "▄", 1, bottom.Color, Color.Default, TextStyle.None);
        }
    }

    /// <summary>The alpha-weighted average colour under one sample, and whether it is mostly opaque.</summary>
    internal static (Color Color, bool Opaque) Sample(ImageData image, int sx, int sy, int columns, int rows)
    {
        var x0 = sx * image.Width / columns;
        var x1 = Math.Min(image.Width, Math.Max(x0 + 1, (sx + 1) * image.Width / columns));
        var y0 = sy * image.Height / rows;
        var y1 = Math.Min(image.Height, Math.Max(y0 + 1, (sy + 1) * image.Height / rows));

        long r = 0;
        long g = 0;
        long b = 0;
        long a = 0;
        long count = 0;
        var data = image.Rgba;
        for (var y = y0; y < y1; y++)
        {
            var row = y * image.Width * 4;
            for (var x = x0; x < x1; x++)
            {
                var i = row + x * 4;
                var alpha = data[i + 3];
                r += data[i] * alpha;
                g += data[i + 1] * alpha;
                b += data[i + 2] * alpha;
                a += alpha;
                count++;
            }
        }
        if (count == 0 || a == 0) return (Color.Default, false);
        var opaque = a / count >= 128;
        return (Color.Rgb((int)(r / a), (int)(g / a), (int)(b / a)), opaque);
    }
}
