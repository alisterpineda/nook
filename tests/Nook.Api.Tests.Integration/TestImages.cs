using SkiaSharp;

namespace Nook.Api.Tests.Integration;

/// <summary>Real images for tests that need a cover something can decode, and reads of the images a response carries.</summary>
internal static class TestImages
{
    /// <summary>A PNG of one colour at the size given.</summary>
    public static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height));
        bitmap.Erase(SKColors.SteelBlue);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// The start of a PNG whose header claims the size given and which holds no pixels, so it costs a few bytes
    /// however large the image it claims to be.
    /// </summary>
    public static byte[] PngClaiming(int width, int height)
    {
        byte[] header =
        [
            (byte)'I', (byte)'H', (byte)'D', (byte)'R',
            (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
            (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height,
            8, 6, 0, 0, 0,
        ];
        var crc = Crc32(header);
        return
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0, 0, 0, 13,
            .. header,
            (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc,
        ];
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFF;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return ~crc;
    }

    /// <summary>The width and height of an image, read from its header without decoding it.</summary>
    public static (int Width, int Height) SizeOf(byte[] image)
    {
        var info = SKBitmap.DecodeBounds(image);
        Assert.False(info.IsEmpty, "The bytes are not an image.");
        return (info.Width, info.Height);
    }

    /// <summary>The format an image is encoded in.</summary>
    public static SKEncodedImageFormat FormatOf(byte[] image)
    {
        using var data = SKData.CreateCopy(image);
        using var codec = SKCodec.Create(data);
        Assert.NotNull(codec);
        return codec.EncodedFormat;
    }
}
