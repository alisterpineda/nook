namespace Nook.Api.Storage;

/// <summary>
/// The image types a cover can be kept as, each with its media type and the extensions its file can have. Ingest
/// names a cover file from this, and the cover is served with the media type found here.
/// </summary>
public static class CoverImageTypes
{
    /// <summary>What a cover whose extension stands for no image type is served as.</summary>
    public const string Fallback = "application/octet-stream";

    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string Gif = "image/gif";
    public const string Webp = "image/webp";
    public const string Svg = "image/svg+xml";

    /// <summary>Each type's media type and extensions. The first extension is the one the media type stands for.</summary>
    private static readonly (string MediaType, string[] Extensions)[] All =
    [
        (Jpeg, [".jpg", ".jpeg"]),
        (Png, [".png"]),
        (Gif, [".gif"]),
        (Webp, [".webp"]),
        (Svg, [".svg"]),
    ];

    private static readonly Dictionary<string, string> MediaTypes = All
        .SelectMany(type => type.Extensions.Select(extension => (extension, type.MediaType)))
        .ToDictionary(pair => pair.extension, pair => pair.MediaType, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> Extensions = All
        .ToDictionary(type => type.MediaType, type => type.Extensions[0], StringComparer.OrdinalIgnoreCase);

    /// <summary>The media type the file's extension stands for, or <see cref="Fallback"/> for any other.</summary>
    public static string MediaTypeOf(string path) => MediaTypes.GetValueOrDefault(Path.GetExtension(path), Fallback);

    /// <summary>Whether the extension, with its dot, is one an image type's file can have, in any case.</summary>
    public static bool IsExtension(string extension) => MediaTypes.ContainsKey(extension);

    /// <summary>The extension the media type stands for, such as <c>.jpg</c>, or null when it is not an image type kept here.</summary>
    public static string? ExtensionOf(string mediaType) => Extensions.GetValueOrDefault(mediaType);
}
