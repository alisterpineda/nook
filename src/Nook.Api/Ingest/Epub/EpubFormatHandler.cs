using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Nook.Api.Data;
using Nook.Api.Storage;
using VersOne.Epub;
using VersOne.Epub.Options;
using VersOne.Epub.Schema;

namespace Nook.Api.Ingest.Epub;

/// <summary>Reads EPUB files. The rest of Nook reaches the EPUB library only through this folder.</summary>
public sealed partial class EpubFormatHandler : IFormatHandler
{
    private const string MimetypeEntry = "mimetype";
    private const string ContainerEntry = "META-INF/container.xml";
    private const string EncryptionEntry = "META-INF/encryption.xml";

    /// <summary>The largest cover read. A larger one is left out, as if the book had none.</summary>
    private const long MaxCoverBytes = 20 * 1024 * 1024;

    /// <summary>The largest <c>META-INF/encryption.xml</c> or <c>META-INF/container.xml</c> read. A larger one makes the book unreadable.</summary>
    private const long MaxOcfFileBytes = 1024 * 1024;

    /// <summary>
    /// The largest markup file the zip may declare. The EPUB library parses the package, navigation and other markup
    /// files itself when it opens a book, so a larger one makes the book unreadable before the library sees it.
    /// </summary>
    private const long MaxMarkupBytes = 10 * 1024 * 1024;

    /// <summary>
    /// The date Calibre writes for a book with no publication date, as <c>0101-01-01T00:00:00+00:00</c>. It always writes
    /// it in UTC, so no other day stands for it.
    /// </summary>
    private static readonly DateOnly CalibreUndefinedDate = new(101, 1, 1);

    /// <summary>The extensions of the entries the EPUB library may parse when it opens a book.</summary>
    private static readonly HashSet<string> MarkupExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".opf", ".ncx", ".xml", ".xhtml", ".html", ".htm",
    };

    /// <summary>The encryption methods that only obfuscate embedded fonts. Anything else in <c>encryption.xml</c> is DRM.</summary>
    private static readonly HashSet<string> FontObfuscationMethods =
    [
        "http://www.idpf.org/2008/embedding",
        "http://ns.adobe.com/pdf/enc#RC",
    ];

    /// <summary>The MARC relator codes of the roles Nook keeps.</summary>
    private static readonly Dictionary<string, ContributorRole> Roles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["aut"] = ContributorRole.Author,
        ["trl"] = ContributorRole.Translator,
        ["edt"] = ContributorRole.Editor,
        ["nrt"] = ContributorRole.Narrator,
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
        var title = metadata.Titles.FirstOrDefault(title => !string.IsNullOrWhiteSpace(title.Title));

        return new BookMetadata(
            Title: title?.Title.Trim(),
            SortTitle: ReadSortTitle(title, metadata.MetaItems),
            Creators: ReadCreators(metadata.Creators, metadata.MetaItems),
            Subjects: Values(metadata.Subjects.Select(subject => subject.Subject)),
            Description: FirstValue(metadata.Descriptions.Select(description => description.Description)),
            Publisher: FirstValue(metadata.Publishers.Select(publisher => publisher.Publisher)),
            Language: FirstValue(metadata.Languages.Select(language => language.Language)),
            PublicationDate: ReadPublicationDate(metadata.Dates),
            Series: ReadSeries(metadata.MetaItems),
            Identifiers: ReadIdentifiers(metadata.Identifiers));
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
            await using var stream = await cover.GetContentStreamAsync();
            var bytes = await ReadCappedAsync(stream, MaxCoverBytes, cancellationToken);
            return bytes is null ? null : new BookCover(bytes, extension);
        }
        catch (EpubReaderException exception)
        {
            throw new UnreadableBookException($"The EPUB's cover cannot be read: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Opens the book without reading its content, after refusing a DRM-encrypted one and one whose markup files the
    /// zip declares larger than the library should parse.
    /// </summary>
    internal static async Task<EpubBookRef> OpenAsync(string path, CancellationToken cancellationToken)
    {
        await CheckArchiveAsync(path, cancellationToken);

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

    /// <summary>
    /// Throws <see cref="UnreadableBookException"/> when the file is not a readable zip archive, when an entry the
    /// library parses declares more than its cap, or when the book is DRM-encrypted.
    /// </summary>
    private static async Task CheckArchiveAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var archive = await ZipFile.OpenReadAsync(path, cancellationToken);
            if (await IsDrmEncryptedAsync(archive, cancellationToken))
            {
                throw new UnreadableBookException("The EPUB is DRM-encrypted, so it cannot be read.");
            }

            await CheckDeclaredSizesAsync(archive, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or XmlException)
        {
            throw new UnreadableBookException($"The EPUB cannot be read: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Refuses a package file or any other markup file the zip declares over <see cref="MaxMarkupBytes"/>. The library
    /// reads these itself, so only the declared size can be checked. The package file is the one the container
    /// names, whatever its extension, and reading the container for it refuses one over <see cref="MaxOcfFileBytes"/>.
    /// </summary>
    private static async Task CheckDeclaredSizesAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        foreach (var entry in archive.Entries)
        {
            if (MarkupExtensions.Contains(Path.GetExtension(entry.FullName)))
            {
                RefuseOversizedMarkup(entry);
            }
        }

        foreach (var packagePath in await ReadPackagePathsAsync(archive, cancellationToken))
        {
            if (archive.GetEntry(packagePath) is { } package)
            {
                RefuseOversizedMarkup(package);
            }
        }
    }

    private static UnreadableBookException TooLarge(string what, long maxBytes) =>
        new($"The EPUB cannot be read: {what} is larger than {maxBytes / 1024 / 1024} MiB.");

    private static void RefuseOversizedMarkup(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxMarkupBytes)
        {
            throw TooLarge(entry.FullName, MaxMarkupBytes);
        }
    }

    /// <summary>
    /// The paths of the package files <c>META-INF/container.xml</c> names, read through the counting read. None when
    /// the archive has no container file, which the library then reports itself.
    /// </summary>
    private static async Task<List<string>> ReadPackagePathsAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        if (archive.GetEntry(ContainerEntry) is not { } entry)
        {
            return [];
        }

        var document = await LoadCappedXmlAsync(entry, "container", cancellationToken);
        return document.Descendants()
            .Where(element => element.Name.LocalName == "rootfile")
            .Select(rootfile => rootfile.Attribute("full-path")?.Value.Trim())
            .Where(fullPath => !string.IsNullOrEmpty(fullPath))
            .Select(fullPath => fullPath!)
            .ToList();
    }

    /// <summary>
    /// Loads one of the container's own small files as XML through the counting read, and throws
    /// <see cref="UnreadableBookException"/> when it is over <see cref="MaxOcfFileBytes"/>.
    /// </summary>
    private static async Task<XDocument> LoadCappedXmlAsync(
        ZipArchiveEntry entry, string what, CancellationToken cancellationToken)
    {
        byte[]? content;
        await using (var stream = await entry.OpenAsync(cancellationToken))
        {
            content = await ReadCappedAsync(stream, MaxOcfFileBytes, cancellationToken);
        }

        if (content is null)
        {
            throw TooLarge($"its {what} file", MaxOcfFileBytes);
        }

        using var reader = XmlReader.Create(new MemoryStream(content), new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
        });
        return await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
    }

    /// <summary>Whether <c>META-INF/encryption.xml</c> lists an encryption method other than font obfuscation.</summary>
    private static async Task<bool> IsDrmEncryptedAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(EncryptionEntry);
        if (entry is null)
        {
            return false;
        }

        var document = await LoadCappedXmlAsync(entry, "encryption", cancellationToken);

        return document.Descendants()
            .Where(element => element.Name.LocalName == "EncryptionMethod")
            .Any(method => !FontObfuscationMethods.Contains(method.Attribute("Algorithm")?.Value.Trim() ?? ""));
    }

    /// <summary>
    /// Reads the stream to its end, counting as it goes, and gives null as soon as it has given more than
    /// <paramref name="maxBytes"/>, without reading the rest.
    /// </summary>
    private static async Task<byte[]?> ReadCappedAsync(Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        using var content = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (content.Length + read > maxBytes)
            {
                return null;
            }

            content.Write(buffer, 0, read);
        }

        return content.ToArray();
    }

    /// <summary>
    /// Calibre's title sort, else an EPUB 3 <c>file-as</c> meta refining the title read. A book with no title has no
    /// sort title.
    /// </summary>
    private static string? ReadSortTitle(EpubMetadataTitle? title, IReadOnlyList<EpubMetadataMeta> metas)
    {
        if (title is null)
        {
            return null;
        }

        return FirstValue(metas.Where(meta => meta.Name == "calibre:title_sort").Select(meta => meta.Content))
            ?? Refining(metas, title.Id, "file-as");
    }

    /// <summary>
    /// The creators with a name, each with its role and sort name from the EPUB 2 <c>opf:role</c> and
    /// <c>opf:file-as</c> attributes, else from EPUB 3 <c>role</c> and <c>file-as</c> metas refining it. A role Nook
    /// does not keep is null.
    /// </summary>
    internal static List<BookCreator> ReadCreators(
        IEnumerable<EpubMetadataCreator> creators, IReadOnlyList<EpubMetadataMeta> metas) =>
        creators
            .Where(creator => !string.IsNullOrWhiteSpace(creator.Creator))
            .Select(creator => new BookCreator(
                creator.Creator.Trim(),
                RoleOf(FirstValue([creator.Role]) ?? Refining(metas, creator.Id, "role")),
                FirstValue([creator.FileAs]) ?? Refining(metas, creator.Id, "file-as")))
            .ToList();

    /// <summary>
    /// The contributor role a MARC relator code stands for, compared ignoring case and surrounding spaces. No code
    /// means an author; a code for a role Nook does not keep gives null.
    /// </summary>
    private static ContributorRole? RoleOf(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? ContributorRole.Author
            : Roles.TryGetValue(code.Trim(), out var role) ? role : null;

    /// <summary>The first value of an EPUB 3 meta with the property that refines the element with the id, if it has one.</summary>
    private static string? Refining(IReadOnlyList<EpubMetadataMeta> metas, string? id, string property) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : FirstValue(metas
                .Where(meta => meta.Refines == "#" + id && meta.Property == property)
                .Select(meta => meta.Content));

    /// <summary>
    /// The first date marked as the publication date or, failing that, the first date with no event, when it is a
    /// complete date. EPUB 3 gives one date with no event, the publication date. Calibre's date for a book with none
    /// gives null.
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
            && value != CalibreUndefinedDate
                ? value
                : null;
    }

    /// <summary>
    /// The series from Calibre's metas, else from the first EPUB 3 collection that is a series, with its position when
    /// that is a number.
    /// </summary>
    private static BookSeries? ReadSeries(IReadOnlyList<EpubMetadataMeta> metas)
    {
        var calibreSeries = FirstValue(metas.Where(meta => meta.Name == "calibre:series").Select(meta => meta.Content));
        if (calibreSeries is not null)
        {
            var index = FirstValue(metas.Where(meta => meta.Name == "calibre:series_index").Select(meta => meta.Content));
            return new BookSeries(calibreSeries, Position(index));
        }

        var collection = metas.FirstOrDefault(meta =>
            meta.Property == "belongs-to-collection"
            && !string.IsNullOrWhiteSpace(meta.Content)
            && IsSeries(Refining(metas, meta.Id, "collection-type")));
        if (collection is null)
        {
            return null;
        }

        var position = Refining(metas, collection.Id, "group-position");
        return new BookSeries(collection.Content.Trim(), Position(position));
    }

    /// <summary>
    /// A collection is a series when its type is <c>series</c>, compared ignoring case and surrounding spaces, or
    /// when it has none. The EPUB specification defines only <c>series</c> and <c>set</c>, and any other type is not
    /// read as a series either.
    /// </summary>
    private static bool IsSeries(string? collectionType) =>
        collectionType is null || string.Equals(collectionType, "series", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The identifiers that name their scheme, by an <c>opf:scheme</c> attribute or else by a <c>urn:&lt;scheme&gt;:</c>
    /// or <c>&lt;scheme&gt;:</c> prefix, with the prefix naming it removed. A URL names no scheme, and an identifier
    /// that names none is left out.
    /// </summary>
    private static List<BookIdentifier> ReadIdentifiers(IEnumerable<EpubMetadataIdentifier> identifiers)
    {
        var result = new List<BookIdentifier>();
        foreach (var identifier in identifiers)
        {
            var value = identifier.Identifier?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            string scheme;
            if (!string.IsNullOrWhiteSpace(identifier.Scheme))
            {
                scheme = identifier.Scheme.Trim();
                var prefix = new[] { $"urn:{scheme}:", $"{scheme}:" }
                    .FirstOrDefault(candidate => value.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));
                if (prefix is not null)
                {
                    value = value[prefix.Length..].Trim();
                }
            }
            else if (SchemePrefix().Match(value) is { Success: true } match)
            {
                scheme = match.Groups["scheme"].Value;
                value = match.Groups["value"].Value.Trim();
            }
            else
            {
                continue;
            }

            if (value.Length > 0)
            {
                result.Add(new BookIdentifier(scheme, value));
            }
        }

        return result;
    }

    private static decimal? Position(string? value) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var position) ? position : null;

    /// <summary>
    /// The cover's extension: the one its file name inside the EPUB has, lowercased, when that is an image's, else the
    /// one its media type stands for, else null. Only an image's extension is given, so a cover is never written
    /// under a book's own name.
    /// </summary>
    private static string? CoverExtension(string filePath, string mediaType)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return CoverImageTypes.IsExtension(extension) ? extension : CoverImageTypes.ExtensionOf(mediaType.Trim());
    }

    /// <summary>The first value that is not blank, trimmed, or null when there is none.</summary>
    internal static string? FirstValue(IEnumerable<string?> values) => Values(values).FirstOrDefault();

    private static List<string> Values(IEnumerable<string?> values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()).ToList();

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2})(?:$|T)")]
    private static partial Regex CompleteDate();

    /// <summary>A <c>urn:&lt;scheme&gt;:</c> or <c>&lt;scheme&gt;:</c> prefix and the value after it. A colon followed by <c>//</c> starts a URL, which names no scheme.</summary>
    [GeneratedRegex(@"^(?:urn:)?(?<scheme>[a-z][a-z0-9._+-]*):(?!//)(?<value>.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SchemePrefix();
}
