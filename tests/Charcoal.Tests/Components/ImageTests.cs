using Charcoal.Components;
using Charcoal.Layout;
using Charcoal.Rendering;

namespace Charcoal.Tests.Components;

public class ImageTests
{
    /// <summary>4×4 PNG: top two rows red, bottom two blue, the last column transparent.</summary>
    private const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAYAAACp8Z5+AAAAIElEQVR4nGP8z8DwnwEKGBkYGJlgHBhgYUQoAKvBUAEAo5UDC06c1PoAAAAASUVORK5CYII=";

    private static HostElement Img(params (string Name, object Value)[] attributes)
    {
        var element = new HostElement("img");
        foreach (var (name, value) in attributes) element.SetAttribute(name, value, 0);
        return element;
    }

    [Fact]
    public void A_data_uri_decodes_and_the_intrinsic_size_is_pixels_over_the_cell_size()
    {
        var node = Assert.IsType<ImageLayoutNode>(Img(("src", Png)).Node);
        var image = node.Image!;
        Assert.Equal((4, 4), (image.Width, image.Height));
        Assert.Equal(new Size(1, 1), FlexLayout.Measure(node, 80, null));
    }

    [Fact]
    public void One_given_side_sets_the_other_from_the_shape_and_no_size_shrinks_to_the_room()
    {
        // 4 columns wide at 8 px a column is 32 px across; the square image is
        // then 32 px tall, two rows at 16 px a row.
        Assert.Equal(new Size(4, 2), FlexLayout.Measure(Img(("src", Png), ("width", 4)).Node, 80, null));
        Assert.Equal(new Size(8, 4), FlexLayout.Measure(Img(("src", Png), ("height", 4)).Node, 80, null));
        Assert.Equal(new Size(4, 6), FlexLayout.Measure(Img(("src", Png), ("width", 4), ("height", 6)).Node, 80, null));

        var wide = new ImageData(800, 200, new byte[800 * 200 * 4]);
        Assert.Equal(new Size(100, 13), ImagePainter.Fit(wide, null, null, null));   // ceil(800/8) × ceil(200/16)
        Assert.Equal(new Size(40, 5), ImagePainter.Fit(wide, null, null, 40));       // 40 columns cover 800 px: 20 px a column, 200 px is 10 samples, 5 cells
    }

    [Fact]
    public void Cells_carry_the_top_pixel_as_foreground_and_the_bottom_as_background()
    {
        var node = (ImageLayoutNode)Img(("src", Png), ("width", 4), ("height", 2)).Node;
        var buffer = new CellBuffer(6, 3);
        node.Paint(buffer, new Rect(1, 1, 4, 2));

        var red = Color.Rgb(255, 0, 0);
        var blue = Color.Rgb(0, 0, 255);
        Assert.Equal(new Cell("▀", 1, red, red, TextStyle.None), buffer[1, 1]);
        Assert.Equal(new Cell("▀", 1, blue, blue, TextStyle.None), buffer[3, 2]);
        Assert.Equal(Cell.Blank, buffer[4, 1]);   // the transparent column paints nothing
        Assert.Equal(Cell.Blank, buffer[0, 0]);
    }

    [Fact]
    public void A_mixed_cell_uses_the_half_block_for_the_opaque_pixel()
    {
        // 1×2 image: opaque top pixel, transparent bottom one, into one cell.
        var image = new ImageData(1, 2, [10, 20, 30, 255, 0, 0, 0, 0]);
        var buffer = new CellBuffer(1, 1);
        ImagePainter.Paint(buffer, new Rect(0, 0, 1, 1), image);
        Assert.Equal(new Cell("▀", 1, Color.Rgb(10, 20, 30), Color.Default, TextStyle.None), buffer[0, 0]);

        var flipped = new ImageData(1, 2, [0, 0, 0, 0, 10, 20, 30, 255]);
        buffer = new CellBuffer(1, 1);
        ImagePainter.Paint(buffer, new Rect(0, 0, 1, 1), flipped);
        Assert.Equal("▄", buffer[0, 0].Cluster);
    }

    [Fact]
    public void A_source_that_does_not_decode_shows_the_alt_text_and_a_new_src_reloads()
    {
        var element = Img(("src", "data:image/png;base64,bm90IGFuIGltYWdl"), ("alt", "[logo]"));
        var node = (ImageLayoutNode)element.Node;
        Assert.Null(node.Image);
        Assert.Equal(new Size(6, 1), FlexLayout.Measure(node, 80, null));
        var buffer = new CellBuffer(8, 1);
        node.Paint(buffer, new Rect(0, 0, 8, 1));
        Assert.Equal("[", buffer[0, 0].Cluster);

        element.SetAttribute("src", Png, 0);
        Assert.True(node.LayoutDirty);
        Assert.NotNull(node.Image);
        Assert.Equal(new Size(1, 1), FlexLayout.Measure(node, 80, null));

        Assert.Null(Img(("src", "/no/such/file.png")).Node is ImageLayoutNode missing ? missing.Image : null);
    }
}
