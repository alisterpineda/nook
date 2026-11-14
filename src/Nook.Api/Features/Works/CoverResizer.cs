using SkiaSharp;

namespace Nook.Api.Features.Works;

/// <summary>Makes the JPEG a cover is served as at a size.</summary>
public class CoverResizer
{
    private const int JpegQuality = 85;

    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);

    /// <summary>
    /// The most pixels a cover can hold and still be decoded. Decoding takes four bytes for each pixel whatever the
    /// file's size, so a small file that claims a huge image could otherwise use up the server's memory.
    /// </summary>
    private const long MaxPixels = 50_000_000;

    /// <summary>
    /// The cover as a JPEG that fits inside the size's box, keeping its aspect ratio. A cover already inside the box
    /// keeps its dimensions: it is never enlarged. Null when the bytes are not an image this can decode, such as an
    /// SVG, or hold more pixels than it will decode.
    /// </summary>
    public byte[]? TryResize(Stream original, CoverSize size)
    {
        using var codec = SKCodec.Create(original);
        if (codec is null)
        {
            return null;
        }

        if ((long)codec.Info.Width * codec.Info.Height > MaxPixels)
        {
            return null;
        }

        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null)
        {
            return null;
        }

        var (boxWidth, boxHeight) = CoverSizes.BoxOf(size);
        var scale = Math.Min((double)boxWidth / decoded.Width, (double)boxHeight / decoded.Height);
        if (scale >= 1)
        {
            return Encode(decoded);
        }

        var width = Math.Max(1, (int)Math.Round(decoded.Width * scale));
        var height = Math.Max(1, (int)Math.Round(decoded.Height * scale));
        using var resized = decoded.Resize(decoded.Info.WithSize(width, height), Sampling);
        return resized is null ? null : Encode(resized);
    }

    private static byte[]? Encode(SKBitmap bitmap)
    {
        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return data?.ToArray();
    }
}
