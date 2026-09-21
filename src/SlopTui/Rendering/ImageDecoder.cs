using StbImageSharp;

namespace SlopTui.Rendering;

/// <summary>
/// Decodes PNG, JPEG, GIF, BMP, TGA and PSD with StbImageSharp, which needs
/// no native library. Failures return null rather than throwing.
/// </summary>
public static class ImageDecoder
{
    public static ImageData? Decode(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var result = ImageResult.FromMemory(bytes.ToArray(), ColorComponents.RedGreenBlueAlpha);
            return result is null ? null : new ImageData(result.Width, result.Height, result.Data);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Loads a base64 <c>data:</c> URI or a file path relative to the working directory.</summary>
    public static ImageData? Load(string src)
    {
        try
        {
            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return LoadDataUri(src);

            var path = src.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ? new Uri(src).LocalPath : src;
            return File.Exists(path) ? Decode(File.ReadAllBytes(path)) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ImageData? LoadDataUri(string uri)
    {
        var comma = uri.IndexOf(',');
        if (comma < 0) return null;

        var header = uri.AsSpan("data:".Length, comma - "data:".Length);
        if (!header.Contains(";base64", StringComparison.OrdinalIgnoreCase)) return null;
        return Decode(Convert.FromBase64String(uri[(comma + 1)..]));
    }
}
