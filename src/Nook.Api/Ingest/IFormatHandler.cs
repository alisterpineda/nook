using Nook.Api.Data;

namespace Nook.Api.Ingest;

/// <summary>
/// One file format ingest can read. Ingest asks every registered handler whether a file is its format, and works
/// with the file only through the handler that says yes.
/// </summary>
public interface IFormatHandler
{
    /// <summary>The format an item made from such a file has.</summary>
    ItemFormat Format { get; }

    /// <summary>The media type such a file is stored with, such as <c>application/epub+zip</c>.</summary>
    string MediaType { get; }

    /// <summary>The extension such a file is filed under, with its leading dot, such as <c>.epub</c>.</summary>
    string Extension { get; }

    /// <summary>Whether the file at <paramref name="path"/> is this format, judged by its content and never by its name.</summary>
    Task<bool> MatchesAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Reads what the file at <paramref name="path"/> says about itself.</summary>
    /// <exception cref="UnreadableBookException">The file is DRM-encrypted or cannot be read.</exception>
    Task<BookMetadata> ReadMetadataAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Reads the cover of the file at <paramref name="path"/>, or null when it has none.</summary>
    /// <exception cref="UnreadableBookException">The file is DRM-encrypted or cannot be read.</exception>
    Task<BookCover?> ReadCoverAsync(string path, CancellationToken cancellationToken = default);
}
