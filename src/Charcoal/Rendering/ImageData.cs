namespace Charcoal.Rendering;

/// <summary>A decoded image: RGBA, eight bits a channel, row-major, top row first.</summary>
public sealed class ImageData
{
    public ImageData(int width, int height, byte[] rgba)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "An image has at least one pixel.");
        }
        if (rgba.Length != width * height * 4)
        {
            throw new ArgumentException("RGBA data does not match the size.", nameof(rgba));
        }
        Width = width;
        Height = height;
        Rgba = rgba;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Rgba { get; }
}
