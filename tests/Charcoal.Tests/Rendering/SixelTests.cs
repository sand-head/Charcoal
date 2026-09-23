using Charcoal.Layout;
using Charcoal.Rendering;

namespace Charcoal.Tests.Rendering;

public class SixelTests
{
    /// <summary>2×2: red across the top, blue bottom-left, transparent bottom-right.</summary>
    private static readonly ImageData Tiny = new(2, 2,
    [
        255, 0, 0, 255,   255, 0, 0, 255,
        0, 0, 255, 255,   0, 0, 0, 0,
    ]);

    [Fact]
    public void A_tiny_image_encodes_its_colours_as_bands_of_bits()
    {
        var sixel = Sixel.Encode(Tiny, 2, 2);
        // Red is level 5 of 6 in red: index 5*42 = 210; blue is level 5 of blue: index 5.
        Assert.StartsWith("\eP0;1;0q\"1;1;2;2#5;2;0;0;100#210;2;100;0;0", sixel);
        // One band: red in row 0 of both columns ('@' = bit 0), then blue in row 1 of column 0 only ('A' = bit 1).
        Assert.EndsWith("#5A$#210@@\e\\", sixel);
    }

    [Fact]
    public void Runs_are_length_encoded_and_bands_are_separated()
    {
        var wide = new ImageData(1, 1, [0, 255, 0, 255]);
        var sixel = Sixel.Encode(wide, 10, 8);   // scaled: 10 wide, 8 tall = two bands
        Assert.Contains("!10~", sixel);          // all six rows of the first band, ten columns
        Assert.Contains("-#", sixel);            // the second band
        Assert.Contains("!10B", sixel);          // rows 6 and 7 of the second band: bits 0 and 1 = 3
    }

    [Fact]
    public void A_window_crops_the_scaled_image_and_drops_unused_colours()
    {
        var sixel = Sixel.Encode(Tiny, 2, 2, new Rect(1, 0, 1, 2));
        Assert.Contains("\"1;1;1;2", sixel);
        Assert.DoesNotContain("#5;", sixel);   // no blue in the right column
        Assert.EndsWith("#210@\e\\", sixel);
        Assert.Equal("", Sixel.Encode(Tiny, 2, 2, new Rect(5, 5, 1, 1)));
    }
}
