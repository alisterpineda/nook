namespace Nook.Api.Features.Works;

/// <summary>The media type a cover file is served with, from its extension.</summary>
public static class CoverMediaTypes
{
    /// <summary>What a cover whose extension stands for no image type is served as.</summary>
    public const string Fallback = "application/octet-stream";

    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string Gif = "image/gif";
    public const string Webp = "image/webp";
    public const string Svg = "image/svg+xml";

    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = Jpeg,
        [".jpeg"] = Jpeg,
        [".png"] = Png,
        [".gif"] = Gif,
        [".webp"] = Webp,
        [".svg"] = Svg,
    };

    /// <summary>The media type the file's extension stands for, or <see cref="Fallback"/> for any other.</summary>
    public static string Of(string path) => ByExtension.GetValueOrDefault(Path.GetExtension(path), Fallback);
}
