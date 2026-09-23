using System.IO.Compression;
using System.Text;
using Charcoal.Layout;

namespace Charcoal.Rendering;

/// <summary>
/// The kitty graphics protocol in Unicode-placeholder mode. An image is sent
/// once with a virtual placement; each cell it covers then holds
/// <c>U+10EEEE</c> with row and column diacritics and the image id as its
/// foreground colour. Because the image lives in ordinary cells, the cell
/// diff handles clipping, scrolling and overlap.
/// </summary>
public static class KittyGraphics
{
    public const string Placeholder = "\U0010EEEE";

    public const int QueryId = 31;

    private const int ChunkSize = 4096;

    /// <summary>
    /// Queries support with a one-pixel image. A supporting terminal answers
    /// <c>ESC _ G i=31;OK ESC \</c>; others stay silent.
    /// </summary>
    public const string Query = "\e_Gi=31,s=1,v=1,a=q,t=d,f=24;AAAA\e\\";

    /// <summary>Whether a reply answers <see cref="Query"/>, and whether it said OK.</summary>
    public static bool IsQueryReply(string sequence, out bool ok)
    {
        ok = false;
        if (!sequence.StartsWith("\e_Gi=31", StringComparison.Ordinal)) return false;
        ok = sequence.Contains(";OK", StringComparison.Ordinal);
        return true;
    }

    /// <summary>The most rows or columns a placement can span, one per diacritic.</summary>
    public static int MaxSpan => Diacritics.Length;

    /// <summary>
    /// Transmits the image as compressed RGBA with a virtual placement of
    /// <paramref name="cols"/> × <paramref name="rows"/>, quietly, in the
    /// base64 chunks the protocol requires.
    /// </summary>
    public static string Transmit(ImageData image, int id, int cols, int rows)
    {
        var encoded = Convert.ToBase64String(Compress(image.Rgba));
        var result = new StringBuilder(encoded.Length + 256);
        var offset = 0;
        do
        {
            var length = Math.Min(ChunkSize, encoded.Length - offset);
            var more = offset + length < encoded.Length;
            result.Append("\e_G");
            if (offset == 0)
            {
                result.Append($"a=T,U=1,q=2,f=32,o=z,i={id},s={image.Width},v={image.Height},c={cols},r={rows},");
            }
            result.Append(more ? "m=1;" : "m=0;");
            result.Append(encoded, offset, length);
            result.Append("\e\\");
            offset += length;
        }
        while (offset < encoded.Length);
        return result.ToString();
    }

    private static byte[] Compress(byte[] data)
    {
        using var stream = new MemoryStream();
        using (var deflate = new ZLibStream(stream, CompressionLevel.Fastest, leaveOpen: true))
        {
            deflate.Write(data);
        }
        return stream.ToArray();
    }

    /// <summary>Frees an image and its placements.</summary>
    public static string Delete(int id) => $"\e_Ga=d,d=I,i={id},q=2\e\\";

    /// <summary>The foreground colour that names an image in its placeholder cells.</summary>
    public static Color IdColor(int id) => Color.Rgb((id >> 16) & 0xFF, (id >> 8) & 0xFF, id & 0xFF);

    public static string Cluster(int row, int col) =>
        Placeholder + char.ConvertFromUtf32(Diacritics[row]) + char.ConvertFromUtf32(Diacritics[col]);

    /// <summary>
    /// Fills the rect with placeholders counted from its own origin, so a
    /// clipped or scrolled image shows the right part.
    /// </summary>
    public static void PaintPlaceholders(CellBuffer buffer, Rect rect, int id)
    {
        var foreground = IdColor(id);
        var rows = Math.Min(rect.Height, MaxSpan);
        var cols = Math.Min(rect.Width, MaxSpan);
        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                buffer.Put(rect.X + col, rect.Y + row, Cluster(row, col), 1, foreground, Color.Default, TextStyle.None);
            }
        }
    }

    /// <summary>Kitty's row and column diacritics: index <c>n</c> is the mark that means <c>n</c>.</summary>
    public static readonly int[] Diacritics =
    [
        0x0305, 0x030D, 0x030E, 0x0310, 0x0312, 0x033D, 0x033E, 0x033F, 0x0346, 0x034A, 0x034B, 0x034C,
        0x0350, 0x0351, 0x0352, 0x0357, 0x035B, 0x0363, 0x0364, 0x0365, 0x0366, 0x0367, 0x0368, 0x0369,
        0x036A, 0x036B, 0x036C, 0x036D, 0x036E, 0x036F, 0x0483, 0x0484, 0x0485, 0x0486, 0x0487, 0x0592,
        0x0593, 0x0594, 0x0595, 0x0597, 0x0598, 0x0599, 0x059C, 0x059D, 0x059E, 0x059F, 0x05A0, 0x05A1,
        0x05A8, 0x05A9, 0x05AB, 0x05AC, 0x05AF, 0x05C4, 0x0610, 0x0611, 0x0612, 0x0613, 0x0614, 0x0615,
        0x0616, 0x0617, 0x0657, 0x0658, 0x0659, 0x065A, 0x065B, 0x065D, 0x065E, 0x06D6, 0x06D7, 0x06D8,
        0x06D9, 0x06DA, 0x06DB, 0x06DC, 0x06DF, 0x06E0, 0x06E1, 0x06E2, 0x06E4, 0x06E7, 0x06E8, 0x06EB,
        0x06EC, 0x0730, 0x0732, 0x0733, 0x0735, 0x0736, 0x073A, 0x073D, 0x073F, 0x0740, 0x0741, 0x0743,
        0x0745, 0x0747, 0x0749, 0x074A, 0x07EB, 0x07EC, 0x07ED, 0x07EE, 0x07EF, 0x07F0, 0x07F1, 0x07F3,
        0x0816, 0x0817, 0x0818, 0x0819, 0x081B, 0x081C, 0x081D, 0x081E, 0x081F, 0x0820, 0x0821, 0x0822,
        0x0823, 0x0825, 0x0826, 0x0827, 0x0829, 0x082A, 0x082B, 0x082C, 0x082D, 0x0951, 0x0953, 0x0954,
        0x0F82, 0x0F83, 0x0F86, 0x0F87, 0x135D, 0x135E, 0x135F, 0x17DD, 0x193A, 0x1A17, 0x1A75, 0x1A76,
        0x1A77, 0x1A78, 0x1A79, 0x1A7A, 0x1A7B, 0x1A7C, 0x1B6B, 0x1B6D, 0x1B6E, 0x1B6F, 0x1B70, 0x1B71,
        0x1B72, 0x1B73, 0x1CD0, 0x1CD1, 0x1CD2, 0x1CDA, 0x1CDB, 0x1CE0, 0x1DC0, 0x1DC1, 0x1DC3, 0x1DC4,
        0x1DC5, 0x1DC6, 0x1DC7, 0x1DC8, 0x1DC9, 0x1DCB, 0x1DCC, 0x1DD1, 0x1DD2, 0x1DD3, 0x1DD4, 0x1DD5,
        0x1DD6, 0x1DD7, 0x1DD8, 0x1DD9, 0x1DDA, 0x1DDB, 0x1DDC, 0x1DDD, 0x1DDE, 0x1DDF, 0x1DE0, 0x1DE1,
        0x1DE2, 0x1DE3, 0x1DE4, 0x1DE5, 0x1DE6, 0x1DFE, 0x20D0, 0x20D1, 0x20D4, 0x20D5, 0x20D6, 0x20D7,
        0x20DB, 0x20DC, 0x20E1, 0x20E7, 0x20E9, 0x20F0, 0x2CEF, 0x2CF0, 0x2CF1, 0x2DE0, 0x2DE1, 0x2DE2,
        0x2DE3, 0x2DE4, 0x2DE5, 0x2DE6, 0x2DE7, 0x2DE8, 0x2DE9, 0x2DEA, 0x2DEB, 0x2DEC, 0x2DED, 0x2DEE,
        0x2DEF, 0x2DF0, 0x2DF1, 0x2DF2, 0x2DF3, 0x2DF4, 0x2DF5, 0x2DF6, 0x2DF7, 0x2DF8, 0x2DF9, 0x2DFA,
        0x2DFB, 0x2DFC, 0x2DFD, 0x2DFE, 0x2DFF, 0xA66F, 0xA67C, 0xA67D, 0xA6F0, 0xA6F1, 0xA8E0, 0xA8E1,
        0xA8E2, 0xA8E3, 0xA8E4, 0xA8E5, 0xA8E6, 0xA8E7, 0xA8E8, 0xA8E9, 0xA8EA, 0xA8EB, 0xA8EC, 0xA8ED,
        0xA8EE, 0xA8EF, 0xA8F0, 0xA8F1, 0xAAB0, 0xAAB2, 0xAAB3, 0xAAB7, 0xAAB8, 0xAABE, 0xAABF, 0xAAC1,
        0xFE20, 0xFE21, 0xFE22, 0xFE23, 0xFE24, 0xFE25, 0xFE26, 0x10A0F, 0x10A38, 0x1D185, 0x1D186, 0x1D187,
        0x1D188, 0x1D189, 0x1D1AA, 0x1D1AB, 0x1D1AC, 0x1D1AD, 0x1D242, 0x1D243, 0x1D244,
    ];
}
