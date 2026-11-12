using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Nook.Api.Data;
using VersOne.Epub;
using VersOne.Epub.Options;
using VersOne.Epub.Schema;

namespace Nook.Api.Ingest.Epub;

/// <summary>Reads EPUB files. The rest of Nook reaches the EPUB library only through this class.</summary>
public sealed partial class EpubFormatHandler : IFormatHandler
{
    private const string MimetypeEntry = "mimetype";
    private const string ContainerEntry = "META-INF/container.xml";
    private const string EncryptionEntry = "META-INF/encryption.xml";

    /// <summary>The encryption methods that only obfuscate embedded fonts. Anything else in <c>encryption.xml</c> is DRM.</summary>
    private static readonly HashSet<string> FontObfuscationMethods =
    [
        "http://www.idpf.org/2008/embedding",
        "http://ns.adobe.com/pdf/enc#RC",
    ];

    private static readonly Dictionary<string, string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
        ["image/svg+xml"] = ".svg",
    };

    public ItemFormat Format => ItemFormat.Epub;

    public string MediaType => "application/epub+zip";

    public string Extension => ".epub";

    /// <summary>
    /// A zip archive whose <c>mimetype</c> entry reads <c>application/epub+zip</c> or, lacking that entry, one that
    /// holds <c>META-INF/container.xml</c>. A file that is not a zip archive, or is a broken one, is not an EPUB.
    /// </summary>
    public async Task<bool> MatchesAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var archive = await ZipFile.OpenReadAsync(path, cancellationToken);
            var mimetype = archive.GetEntry(MimetypeEntry);
            if (mimetype is null)
            {
                return archive.GetEntry(ContainerEntry) is not null;
            }

            // A real mimetype entry is twenty bytes; a large one is not worth reading.
            if (mimetype.Length > 1024)
            {
                return false;
            }

            await using var stream = await mimetype.OpenAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.ASCII);
            var content = await reader.ReadToEndAsync(cancellationToken);
            return content.Trim() == MediaType;
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
        {
            return false;
        }
    }

    public async Task<BookMetadata> ReadMetadataAsync(string path, CancellationToken cancellationToken = default)
    {
        using var book = await OpenAsync(path, cancellationToken);
        var metadata = book.Schema.Package.Metadata;

        return new BookMetadata(
            Title: FirstValue(metadata.Titles.Select(title => title.Title)),
            Creators: Values(metadata.Creators.Select(creator => creator.Creator)),
            Subjects: Values(metadata.Subjects.Select(subject => subject.Subject)),
            Description: FirstValue(metadata.Descriptions.Select(description => description.Description)),
            Publisher: FirstValue(metadata.Publishers.Select(publisher => publisher.Publisher)),
            Language: FirstValue(metadata.Languages.Select(language => language.Language)),
            PublicationDate: ReadPublicationDate(metadata.Dates),
            Series: ReadSeries(metadata.MetaItems));
    }

    public async Task<BookCover?> ReadCoverAsync(string path, CancellationToken cancellationToken = default)
    {
        using var book = await OpenAsync(path, cancellationToken);
        var cover = book.Content.Cover;
        if (cover is null)
        {
            return null;
        }

        var extension = CoverExtension(cover.FilePath, cover.ContentMimeType);
        if (extension is null)
        {
            return null;
        }

        try
        {
            return new BookCover(await cover.ReadContentAsync(), extension);
        }
        catch (EpubReaderException exception)
        {
            throw new UnreadableBookException($"The EPUB's cover cannot be read: {exception.Message}", exception);
        }
    }

    /// <summary>Opens the book without reading its content, after refusing a DRM-encrypted one.</summary>
    private static async Task<EpubBookRef> OpenAsync(string path, CancellationToken cancellationToken)
    {
        if (await IsDrmEncryptedAsync(path, cancellationToken))
        {
            throw new UnreadableBookException("The EPUB is DRM-encrypted, so it cannot be read.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        EpubBookRef? book;
        try
        {
            book = await EpubReader.OpenBookAsync(path, new EpubReaderOptions(EpubReaderOptionsPreset.RELAXED));
        }
        catch (EpubReaderException exception)
        {
            throw new UnreadableBookException($"The EPUB cannot be read: {exception.Message}", exception);
        }

        return book ?? throw new UnreadableBookException("The EPUB cannot be read: it has no package file.");
    }

    /// <summary>Whether <c>META-INF/encryption.xml</c> lists an encryption method other than font obfuscation.</summary>
    private static async Task<bool> IsDrmEncryptedAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var archive = await ZipFile.OpenReadAsync(path, cancellationToken);
            var entry = archive.GetEntry(EncryptionEntry);
            if (entry is null)
            {
                return false;
            }

            await using var stream = await entry.OpenAsync(cancellationToken);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
            });
            var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);

            return document.Descendants()
                .Where(element => element.Name.LocalName == "EncryptionMethod")
                .Any(method => !FontObfuscationMethods.Contains(method.Attribute("Algorithm")?.Value.Trim() ?? ""));
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or XmlException)
        {
            throw new UnreadableBookException($"The EPUB cannot be read: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// The first date marked as the publication date or, failing that, the first date with no event, when it is a
    /// complete date. EPUB 3 gives one date with no event, the publication date.
    /// </summary>
    private static DateOnly? ReadPublicationDate(IEnumerable<EpubMetadataDate> dates)
    {
        var candidates = dates
            .Where(date => string.IsNullOrWhiteSpace(date.Event)
                || string.Equals(date.Event.Trim(), "publication", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var date = candidates.FirstOrDefault(date => !string.IsNullOrWhiteSpace(date.Event)) ?? candidates.FirstOrDefault();
        if (date is null)
        {
            return null;
        }

        // A date with a time keeps the day as written; a bare year or year and month is not a complete date.
        var match = CompleteDate().Match(date.Date.Trim());
        return match.Success
            && DateOnly.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
                ? value
                : null;
    }

    /// <summary>The series from Calibre's metas, else from the first EPUB 3 collection, with its position when that is a number.</summary>
    private static BookSeries? ReadSeries(IReadOnlyList<EpubMetadataMeta> metas)
    {
        var calibreSeries = FirstValue(metas.Where(meta => meta.Name == "calibre:series").Select(meta => meta.Content));
        if (calibreSeries is not null)
        {
            var index = FirstValue(metas.Where(meta => meta.Name == "calibre:series_index").Select(meta => meta.Content));
            return new BookSeries(calibreSeries, Position(index));
        }

        var collection = metas.FirstOrDefault(meta =>
            meta.Property == "belongs-to-collection" && !string.IsNullOrWhiteSpace(meta.Content));
        if (collection is null)
        {
            return null;
        }

        var position = collection.Id is null
            ? null
            : FirstValue(metas
                .Where(meta => meta.Refines == "#" + collection.Id && meta.Property == "group-position")
                .Select(meta => meta.Content));
        return new BookSeries(collection.Content.Trim(), Position(position));
    }

    private static decimal? Position(string? value) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var position) ? position : null;

    /// <summary>The cover's extension from its file name inside the EPUB, lowercased, else from its media type.</summary>
    private static string? CoverExtension(string filePath, string mediaType)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (SafeExtension().IsMatch(extension))
        {
            return extension;
        }

        return ImageExtensions.GetValueOrDefault(mediaType.Trim());
    }

    private static string? FirstValue(IEnumerable<string?> values) => Values(values).FirstOrDefault();

    private static List<string> Values(IEnumerable<string?> values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()).ToList();

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2})(?:$|T)")]
    private static partial Regex CompleteDate();

    [GeneratedRegex(@"^\.[a-z0-9]{1,10}$")]
    private static partial Regex SafeExtension();
}
