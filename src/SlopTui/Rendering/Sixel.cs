using System.Text;
using SlopTui.Layout;

namespace SlopTui.Rendering;

/// <summary>
/// Encodes an image as Sixel graphics: the DEC picture format that xterm,
/// foot, mlterm, WezTerm, Contour and others draw at the cursor. The image
/// is scaled to a pixel size and quantised to a 252-colour palette (six
/// levels of red and blue, seven of green); a mostly transparent pixel is
/// not drawn at all, which with the <c>P2=1</c> parameter leaves the cell
/// beneath it as it was.
/// </summary>
/// <remarks>
/// <para>
/// The format: <c>DCS P1 ; P2 ; P3 q</c>, a raster attribute
/// (<c>"1;1;w;h</c>), colour definitions (<c>#n;2;r;g;b</c> in percent),
/// then bands of six pixel rows. In a band, each colour in turn selects
/// itself (<c>#n</c>) and writes one character a column whose six bits say
/// which of the band's rows are that colour; <c>$</c> returns to the
/// band's start for the next colour, <c>-</c> moves to the next band; a
/// run of equal characters is <c>!count</c> and the character. <c>ST</c>
/// ends it.
/// </para>
/// <para>
/// A Sixel picture is not bound to cells the way a kitty placement is:
/// the terminal paints the pixels and forgets, so anything written over
/// those cells later erases part of it. The app therefore re-sends a
/// picture whenever the frame diff touched its rows, and crops it to the
/// cells that are visible, since the terminal cannot clip it either.
/// </para>
/// </remarks>
public static class Sixel
{
    /// <summary>The encoded picture: the image scaled to <paramref name="pixelWidth"/> × <paramref name="pixelHeight"/>, whole.</summary>
    public static string Encode(ImageData image, int pixelWidth, int pixelHeight) =>
        Encode(image, pixelWidth, pixelHeight, new Rect(0, 0, pixelWidth, pixelHeight));

    /// <summary>
    /// The encoded picture for one <paramref name="window"/> of the scaled
    /// image, in the scaled image's pixels: what a partly visible picture
    /// sends for the part that shows.
    /// </summary>
    public static string Encode(ImageData image, int pixelWidth, int pixelHeight, Rect window)
    {
        window = window.Intersect(new Rect(0, 0, pixelWidth, pixelHeight));
        if (window.IsEmpty || image.Width == 0 || image.Height == 0) return "";

        // Quantise every window pixel once: -1 for a pixel that is not drawn.
        var width = window.Width;
        var height = window.Height;
        var indices = new short[width * height];
        var used = new bool[252];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (color, opaque) = ImagePainter.Sample(image, window.X + x, window.Y + y, pixelWidth, pixelHeight);
                if (!opaque) { indices[y * width + x] = -1; continue; }
                var index = Quantise(color);
                indices[y * width + x] = (short)index;
                used[index] = true;
            }
        }

        var sb = new StringBuilder();
        // P1 = 0 (aspect from the raster attribute), P2 = 1 (undrawn pixels keep what is there), P3 = 0.
        sb.Append("\eP0;1;0q");
        sb.Append("\"1;1;").Append(width).Append(';').Append(height);
        for (var i = 0; i < used.Length; i++)
        {
            if (!used[i]) continue;
            var (r, g, b) = Palette(i);
            sb.Append('#').Append(i).Append(";2;").Append(r * 100 / 255).Append(';').Append(g * 100 / 255).Append(';').Append(b * 100 / 255);
        }

        var column = new byte[width];
        for (var top = 0; top < height; top += 6)
        {
            var rows = Math.Min(6, height - top);
            var first = true;
            for (var color = 0; color < used.Length; color++)
            {
                if (!used[color]) continue;
                var any = false;
                for (var x = 0; x < width; x++)
                {
                    byte bits = 0;
                    for (var r = 0; r < rows; r++)
                        if (indices[(top + r) * width + x] == color) bits |= (byte)(1 << r);
                    column[x] = bits;
                    any |= bits != 0;
                }
                if (!any) continue;
                if (!first) sb.Append('$');
                first = false;
                sb.Append('#').Append(color);
                // Run-length encode the columns; a trailing run of empty columns need not be written.
                var last = width - 1;
                while (last >= 0 && column[last] == 0) last--;
                for (var x = 0; x <= last;)
                {
                    var run = 1;
                    while (x + run <= last && column[x + run] == column[x]) run++;
                    var ch = (char)(63 + column[x]);
                    if (run > 3) sb.Append('!').Append(run).Append(ch);
                    else sb.Append(ch, run);
                    x += run;
                }
            }
            if (top + 6 < height) sb.Append('-');
        }
        sb.Append("\e\\");
        return sb.ToString();
    }

    /// <summary>The palette index of a colour: 6 × 7 × 6 levels, red, green, blue.</summary>
    private static int Quantise(Color color)
    {
        var (r, g, b) = color.Kind == ColorKind.Rgb ? (color.R, color.G, color.B) : ((byte)0, (byte)0, (byte)0);
        var ri = (r * 5 + 127) / 255;
        var gi = (g * 6 + 127) / 255;
        var bi = (b * 5 + 127) / 255;
        return ri * 42 + gi * 6 + bi;
    }

    /// <summary>The colour a palette index stands for.</summary>
    private static (int R, int G, int B) Palette(int index)
    {
        var ri = index / 42;
        var gi = index % 42 / 6;
        var bi = index % 6;
        return (ri * 255 / 5, gi * 255 / 6, bi * 255 / 5);
    }
}
