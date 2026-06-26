using System.Buffers.Binary;

namespace Mdv.Diagrams;

internal static class ImageInfo
{
    /// <summary>Reads width/height from a PNG's IHDR chunk without decoding the image.</summary>
    public static (int Width, int Height) GetPngSize(byte[] png)
    {
        // PNG signature (8) + length (4) + "IHDR" (4) + width (4) + height (4)
        if (png.Length < 24) return (0, 0);
        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        return (width, height);
    }
}
