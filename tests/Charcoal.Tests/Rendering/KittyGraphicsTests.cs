using System.IO.Compression;
using Charcoal.Layout;
using Charcoal.Rendering;

namespace Charcoal.Tests.Rendering;

public class KittyGraphicsTests
{
    [Fact]
    public void A_transmission_is_chunked_base64_of_deflated_rgba_with_a_virtual_placement()
    {
        // 300×20 RGBA is 24000 bytes: enough that the base64 of even the
        // compressed noise spans several 4096-byte chunks.
        var rng = new Random(7);
        var rgba = new byte[300 * 20 * 4];
        rng.NextBytes(rgba);
        var image = new ImageData(300, 20, rgba);

        var text = KittyGraphics.Transmit(image, 0x010203, 30, 2);
        var chunks = text.Split("\e\\", StringSplitOptions.RemoveEmptyEntries);
        Assert.True(chunks.Length > 1, text.Length.ToString());
        Assert.All(chunks, c => Assert.StartsWith("\e_G", c));

        var first = chunks[0];
        Assert.Contains("a=T,U=1,q=2,f=32,o=z,i=66051,s=300,v=20,c=30,r=2,m=1;", first);
        Assert.All(chunks[1..^1], c => Assert.StartsWith("\e_Gm=1;", c));
        Assert.StartsWith("\e_Gm=0;", chunks[^1]);

        var payload = string.Concat(chunks.Select(c => c[(c.IndexOf(';') + 1)..]));
        Assert.All(chunks, c => Assert.True(c.Length - c.IndexOf(';') - 1 <= 4096));
        using var stream = new MemoryStream(Convert.FromBase64String(payload));
        using var inflate = new ZLibStream(stream, CompressionMode.Decompress);
        using var result = new MemoryStream();
        inflate.CopyTo(result);
        Assert.Equal(rgba, result.ToArray());
    }

    [Fact]
    public void Placeholder_cells_carry_the_id_as_colour_and_the_row_and_column_as_diacritics()
    {
        var buffer = new CellBuffer(5, 4);
        KittyGraphics.PaintPlaceholders(buffer, new Rect(1, 1, 3, 2), 42);

        var cell = buffer[1, 1];
        Assert.Equal(Color.Rgb(0, 0, 42), cell.Foreground);
        Assert.Equal(KittyGraphics.Placeholder + "̅̅", cell.Cluster);   // row 0, column 0
        Assert.Equal(KittyGraphics.Placeholder + "̍̎", buffer[3, 2].Cluster);   // row 1, column 2
        Assert.Equal(1, cell.Width);
        Assert.Equal(Cell.Blank, buffer[0, 0]);
        Assert.Equal(Cell.Blank, buffer[4, 1]);

        Assert.Equal(Color.Rgb(1, 2, 3), KittyGraphics.IdColor(0x010203));
        Assert.Equal(297, KittyGraphics.MaxSpan);
        Assert.Equal(0x1D244, KittyGraphics.Diacritics[^1]);
    }

    [Fact]
    public void The_query_reply_is_recognised_and_delete_names_the_image()
    {
        Assert.True(KittyGraphics.IsQueryReply("\e_Gi=31;OK\e\\", out var ok));
        Assert.True(ok);
        Assert.True(KittyGraphics.IsQueryReply("\e_Gi=31;EINVAL:bad\e\\", out ok));
        Assert.False(ok);
        Assert.False(KittyGraphics.IsQueryReply("\e[?62;4c", out _));
        Assert.Equal("\e_Ga=d,d=I,i=7,q=2\e\\", KittyGraphics.Delete(7));
    }

    [Fact]
    public void The_terminal_cell_size_drives_the_intrinsic_size()
    {
        var image = new ImageData(100, 100, new byte[100 * 100 * 4]);
        Assert.Equal(new Size(13, 7), ImagePainter.Fit(image, null, null, null));            // 8×16 assumed
        Assert.Equal(new Size(10, 5), ImagePainter.Fit(image, null, null, null, 10, 20));    // reported 10×20
        Assert.Equal(new Size(20, 10), ImagePainter.Fit(image, 20, null, null, 10, 20));
    }
}
