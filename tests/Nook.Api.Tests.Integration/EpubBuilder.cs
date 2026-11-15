using System.IO.Compression;
using System.Security;
using System.Text;

namespace Nook.Api.Tests.Integration;

/// <summary>
/// Builds an EPUB file in memory: a package with the metadata, cover, encryption, chapters and other files asked for
/// (one chapter when none is), and the table of contents its version expects. Raw overrides replace or remove any
/// file, for tests of broken books.
/// </summary>
internal sealed class EpubBuilder
{
    /// <summary>The IDPF font obfuscation algorithm, which is not DRM.</summary>
    public const string IdpfFontObfuscation = "http://www.idpf.org/2008/embedding";

    /// <summary>Adobe's font obfuscation algorithm, which is not DRM.</summary>
    public const string AdobeFontObfuscation = "http://ns.adobe.com/pdf/enc#RC";

    /// <summary>AES-128 in CBC mode, the algorithm DRM schemes encrypt content with.</summary>
    public const string Aes128Cbc = "http://www.w3.org/2001/04/xmlenc#aes128-cbc";

    private const string PackageFolder = "OEBPS/";
    private const string PackagePath = PackageFolder + "content.opf";

    private readonly int _version;
    private readonly List<string> _metadata = [];
    private readonly List<string> _manifest = [];
    private readonly List<(string Href, string Body)> _chapters = [];
    private readonly List<(string Href, byte[] Content)> _manifestFiles = [];
    private IReadOnlyList<Contents>? _contents;
    private readonly Dictionary<string, byte[]> _files = [];
    private readonly HashSet<string> _removed = [];
    private string? _uniqueIdentifier;
    private int _nextId = 1;

    /// <param name="version">The EPUB version, 2 or 3. It decides how creators' roles and sort names and the cover are declared.</param>
    public EpubBuilder(int version = 3)
    {
        if (version is not (2 or 3))
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "An EPUB is version 2 or 3.");
        }

        _version = version;
    }

    /// <summary>Adds a <c>dc:title</c>, with an id when one is given so a meta can refine it.</summary>
    public EpubBuilder WithTitle(string title, string? id = null) =>
        AddMetadata($"<dc:title{Attribute("id", id)}>{Escape(title)}</dc:title>");

    /// <summary>
    /// Adds a <c>dc:creator</c>. EPUB 2 gives its role and sort name as <c>opf:role</c> and <c>opf:file-as</c>
    /// attributes; EPUB 3 gives them as metas refining the creator.
    /// </summary>
    public EpubBuilder WithCreator(string name, string? role = null, string? fileAs = null)
    {
        if (_version == 2)
        {
            return AddMetadata(
                $"<dc:creator{Attribute("opf:role", role)}{Attribute("opf:file-as", fileAs)}>{Escape(name)}</dc:creator>");
        }

        var id = NextId("creator");
        AddMetadata($"<dc:creator id=\"{id}\">{Escape(name)}</dc:creator>");
        if (role is not null)
        {
            AddMetadata($"<meta refines=\"#{id}\" property=\"role\" scheme=\"marc:relators\">{Escape(role)}</meta>");
        }

        if (fileAs is not null)
        {
            AddMetadata($"<meta refines=\"#{id}\" property=\"file-as\">{Escape(fileAs)}</meta>");
        }

        return this;
    }

    /// <summary>Adds a <c>dc:identifier</c>, with an <c>opf:scheme</c> attribute when a scheme is given. The first one is the package's unique identifier.</summary>
    public EpubBuilder WithIdentifier(string value, string? scheme = null)
    {
        var id = NextId("identifier");
        _uniqueIdentifier ??= id;
        return AddMetadata($"<dc:identifier id=\"{id}\"{Attribute("opf:scheme", scheme)}>{Escape(value)}</dc:identifier>");
    }

    public EpubBuilder WithSubject(string subject) => AddMetadata($"<dc:subject>{Escape(subject)}</dc:subject>");

    public EpubBuilder WithDescription(string description) =>
        AddMetadata($"<dc:description>{Escape(description)}</dc:description>");

    public EpubBuilder WithPublisher(string publisher) => AddMetadata($"<dc:publisher>{Escape(publisher)}</dc:publisher>");

    public EpubBuilder WithLanguage(string language) => AddMetadata($"<dc:language>{Escape(language)}</dc:language>");

    /// <summary>Adds a <c>dc:date</c>, with an <c>opf:event</c> attribute, as EPUB 2 allows, when an event is given.</summary>
    public EpubBuilder WithDate(string date, string? eventName = null) =>
        AddMetadata($"<dc:date{Attribute("opf:event", eventName)}>{Escape(date)}</dc:date>");

    /// <summary>Adds a meta in the EPUB 2 form, <c>&lt;meta name content/&gt;</c>, which is how Calibre writes its <c>calibre:*</c> values.</summary>
    public EpubBuilder WithMeta(string name, string content) =>
        AddMetadata($"<meta name=\"{Escape(name)}\" content=\"{Escape(content)}\"/>");

    /// <summary>Adds Calibre's series and series index metas.</summary>
    public EpubBuilder WithCalibreSeries(string name, string index) =>
        WithMeta("calibre:series", name).WithMeta("calibre:series_index", index);

    /// <summary>Adds a meta in the EPUB 3 form, <c>&lt;meta property&gt;value&lt;/meta&gt;</c>, refining another element when <paramref name="refines"/> is its id.</summary>
    public EpubBuilder WithProperty(string property, string value, string? id = null, string? refines = null) =>
        AddMetadata(
            $"<meta{Attribute("id", id)}{Attribute("refines", refines is null ? null : "#" + refines)} property=\"{Escape(property)}\">{Escape(value)}</meta>");

    /// <summary>
    /// Adds an EPUB 3 <c>belongs-to-collection</c> with, when given, its <c>collection-type</c> and its
    /// <c>group-position</c>.
    /// </summary>
    public EpubBuilder WithCollection(string name, string? position = null, string? type = null)
    {
        var id = NextId("collection");
        WithProperty("belongs-to-collection", name, id: id);
        if (type is not null)
        {
            WithProperty("collection-type", type, refines: id);
        }

        return position is null ? this : WithProperty("group-position", position, refines: id);
    }

    /// <summary>
    /// Adds a cover image under <c>OEBPS/images/</c>, declared the way the builder's version does it: EPUB 2 with
    /// <c>&lt;meta name="cover"&gt;</c>, EPUB 3 with the <c>cover-image</c> property.
    /// </summary>
    public EpubBuilder WithCover(byte[] bytes, string fileName = "cover.png", string mediaType = "image/png")
    {
        var href = "images/" + fileName;
        _files["OEBPS/" + href] = bytes;
        if (_version == 2)
        {
            _manifest.Add($"<item id=\"cover-image\" href=\"{Escape(href)}\" media-type=\"{Escape(mediaType)}\"/>");
            return WithMeta("cover", "cover-image");
        }

        _manifest.Add(
            $"<item id=\"cover-image\" href=\"{Escape(href)}\" media-type=\"{Escape(mediaType)}\" properties=\"cover-image\"/>");
        return this;
    }

    /// <summary>Adds <c>META-INF/encryption.xml</c> declaring the chapter encrypted, once with each algorithm given.</summary>
    public EpubBuilder WithEncryption(params string[] algorithms)
    {
        var entries = algorithms.Select(algorithm => $"""
              <enc:EncryptedData>
                <enc:EncryptionMethod Algorithm="{Escape(algorithm)}"/>
                <enc:CipherData><enc:CipherReference URI="OEBPS/chapter.xhtml"/></enc:CipherData>
              </enc:EncryptedData>
            """);
        return WithFile("META-INF/encryption.xml", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <encryption xmlns="urn:oasis:names:tc:opendocument:xmlns:container" xmlns:enc="http://www.w3.org/2001/04/xmlenc#">
            {string.Join("\n", entries)}
            </encryption>
            """);
    }

    /// <summary>
    /// Adds a chapter to the spine, after the ones added before it, at a path relative to the package folder. The
    /// first one added replaces the chapter the builder writes when none is.
    /// </summary>
    /// <param name="body">The markup inside the chapter's <c>body</c> element.</param>
    public EpubBuilder WithChapter(string path, string body)
    {
        _chapters.Add((path, body));
        return this;
    }

    /// <summary>Adds a file declared in the package manifest but not in the spine, at a path relative to the package folder.</summary>
    public EpubBuilder WithManifestItem(string path, string content, string mediaType) =>
        WithManifestItem(path, Encoding.UTF8.GetBytes(content), mediaType);

    /// <inheritdoc cref="WithManifestItem(string, string, string)"/>
    public EpubBuilder WithManifestItem(string path, byte[] content, string mediaType)
    {
        _manifestFiles.Add((path, content));
        _manifest.Add($"<item id=\"{NextId("item")}\" href=\"{Escape(path)}\" media-type=\"{Escape(mediaType)}\"/>");
        return this;
    }

    /// <summary>
    /// Sets the table of contents, written to the nav document in EPUB 3 and to the NCX in EPUB 2, in place of the one
    /// entry for the builder's own chapter.
    /// </summary>
    public EpubBuilder WithContents(params Contents[] entries)
    {
        _contents = entries;
        return this;
    }

    /// <summary>
    /// An entry in the table of contents.
    /// </summary>
    /// <param name="Title">The entry's text.</param>
    /// <param name="Href">The target relative to the package folder, with any fragment, or null for a heading with no link (EPUB 3 only).</param>
    /// <param name="Children">The entries nested under it.</param>
    public sealed record Contents(string Title, string? Href, params Contents[] Children);

    /// <summary>Puts a file in the archive as given, replacing the one the builder would write at that path.</summary>
    public EpubBuilder WithFile(string path, string content) => WithFile(path, Encoding.UTF8.GetBytes(content));

    /// <inheritdoc cref="WithFile(string, string)"/>
    public EpubBuilder WithFile(string path, byte[] content)
    {
        _files[path] = content;
        _removed.Remove(path);
        return this;
    }

    /// <summary>Leaves a file out of the archive, including one the builder would write.</summary>
    public EpubBuilder WithoutFile(string path)
    {
        _files.Remove(path);
        _removed.Add(path);
        return this;
    }

    public byte[] Build()
    {
        var files = new List<(string Path, byte[] Content)>
        {
            ("mimetype", Encoding.ASCII.GetBytes("application/epub+zip")),
            ("META-INF/container.xml", Encoding.UTF8.GetBytes(ContainerXml)),
            (PackagePath, Encoding.UTF8.GetBytes(PackageXml())),
        };
        files.AddRange(Chapters.Select(chapter =>
            (PackageFolder + chapter.Href, Encoding.UTF8.GetBytes(ChapterXhtml(chapter.Body)))));
        files.Add(_version == 2
            ? (PackageFolder + "toc.ncx", Encoding.UTF8.GetBytes(Ncx()))
            : (PackageFolder + "nav.xhtml", Encoding.UTF8.GetBytes(NavXhtml())));
        files.AddRange(_manifestFiles.Select(file => (PackageFolder + file.Href, file.Content)));

        // An override replaces the built file in place, so the mimetype entry stays first when it is overridden.
        for (var i = 0; i < files.Count; i++)
        {
            if (_files.TryGetValue(files[i].Path, out var content))
            {
                files[i] = (files[i].Path, content);
            }
        }

        files.AddRange(_files.Where(file => files.All(built => built.Path != file.Key)).Select(file => (file.Key, file.Value)));
        files.RemoveAll(file => _removed.Contains(file.Path));

        return Zip(files);
    }

    /// <summary>Writes the files into a zip archive in the order given, with an entry named <c>mimetype</c> stored uncompressed, as the OCF rules ask.</summary>
    public static byte[] Zip(IEnumerable<(string Path, byte[] Content)> files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                var level = path == "mimetype" ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                using var entry = archive.CreateEntry(path, level).Open();
                entry.Write(content);
            }
        }

        return stream.ToArray();
    }

    /// <summary>The chapters added, or the builder's own one when none is.</summary>
    private IReadOnlyList<(string Href, string Body)> Chapters =>
        _chapters.Count > 0 ? _chapters : [("chapter.xhtml", "<p>Once upon a time.</p>")];

    /// <summary>The table of contents set, or one entry for the first chapter when none is.</summary>
    private IReadOnlyList<Contents> ContentsEntries => _contents ?? [new Contents("Chapter", Chapters[0].Href)];

    private string PackageXml()
    {
        var uniqueIdentifier = _uniqueIdentifier is null ? "" : $" unique-identifier=\"{_uniqueIdentifier}\"";
        var tocItem = _version == 2
            ? "<item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>"
            : "<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>";
        var spineToc = _version == 2 ? " toc=\"ncx\"" : "";
        var chapterItems = Chapters.Select((chapter, index) =>
            $"<item id=\"chapter{index + 1}\" href=\"{Escape(chapter.Href)}\" media-type=\"application/xhtml+xml\"/>");
        var itemrefs = Chapters.Select((_, index) => $"<itemref idref=\"chapter{index + 1}\"/>");
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="{_version}.0"{uniqueIdentifier}>
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:opf="http://www.idpf.org/2007/opf">
                {string.Join("\n    ", _metadata)}
              </metadata>
              <manifest>
                {string.Join("\n    ", chapterItems)}
                {tocItem}
                {string.Join("\n    ", _manifest)}
              </manifest>
              <spine{spineToc}>
                {string.Join("\n    ", itemrefs)}
              </spine>
            </package>
            """;
    }

    private const string ContainerXml = $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
          <rootfiles>
            <rootfile full-path="{PackagePath}" media-type="application/oebps-package+xml"/>
          </rootfiles>
        </container>
        """;

    private static string ChapterXhtml(string body) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml"><head><title>Chapter</title></head><body>{body}</body></html>
        """;

    private string Ncx()
    {
        var playOrder = 0;
        string NavPoint(Contents entry)
        {
            var src = entry.Href ?? throw new InvalidOperationException("An NCX entry needs a link.");
            var id = ++playOrder;
            return $"<navPoint id=\"point{id}\" playOrder=\"{id}\"><navLabel><text>{Escape(entry.Title)}</text></navLabel>"
                + $"<content src=\"{Escape(src)}\"/>{string.Concat(entry.Children.Select(NavPoint))}</navPoint>";
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
              <head/>
              <docTitle><text>Book</text></docTitle>
              <navMap>{string.Concat(ContentsEntries.Select(NavPoint))}</navMap>
            </ncx>
            """;
    }

    private string NavXhtml()
    {
        static string List(IEnumerable<Contents> entries) =>
            $"<ol>{string.Concat(entries.Select(entry =>
                $"<li>{(entry.Href is null
                    ? $"<span>{Escape(entry.Title)}</span>"
                    : $"<a href=\"{Escape(entry.Href)}\">{Escape(entry.Title)}</a>")}"
                + $"{(entry.Children.Length > 0 ? List(entry.Children) : "")}</li>"))}</ol>";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
              <head><title>Contents</title></head>
              <body><nav epub:type="toc"><h1>Contents</h1>{List(ContentsEntries)}</nav></body>
            </html>
            """;
    }

    private EpubBuilder AddMetadata(string element)
    {
        _metadata.Add(element);
        return this;
    }

    private string NextId(string prefix) => $"{prefix}{_nextId++}";

    private static string Attribute(string name, string? value) =>
        value is null ? "" : $" {name}=\"{Escape(value)}\"";

    private static string Escape(string value) => SecurityElement.Escape(value);
}
