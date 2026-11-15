using System.IO.Compression;
using Nook.Api.Ingest;
using Nook.Api.Ingest.Epub;
using Contents = Nook.Api.Tests.Integration.EpubBuilder.Contents;

namespace Nook.Api.Tests.Integration;

public sealed class EpubPublicationReaderTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("nook-publication-tests-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Publication_lists_the_spine_in_order_and_every_other_file_as_a_resource()
    {
        var path = await WriteAsync(new EpubBuilder()
            .WithTitle("Piranesi")
            .WithIdentifier("urn:isbn:9781635575637")
            .WithLanguage("en-GB")
            .WithCreator("Susanna Clarke", role: "aut")
            .WithCreator("Someone Reading", role: "nrt")
            .WithCreator("No Role")
            .WithChapter("text/part 2.xhtml", "<p>Second in the archive, first in the spine.</p>")
            .WithChapter("text/part1.xhtml", "<p>" + new string('x', 5000) + "</p>")
            .WithManifestItem("styles/book.css", "p { margin: 0; }", "text/css")
            .WithManifestItem("images/plate.png", TestData.PngCover, "image/png")
            .WithFile("OEBPS/unlisted.xhtml", "<html/>")
            .Build());

        var publication = await EpubPublicationReader.ReadAsync(path, CancellationToken);

        Assert.Equal("Piranesi", publication.Title);
        Assert.Equal("urn:isbn:9781635575637", publication.Identifier);
        Assert.Equal("en-GB", publication.Language);
        Assert.Equal(["Susanna Clarke", "No Role"], publication.Authors);

        Assert.Equal(
            [
                new EpubResource("OEBPS/text/part 2.xhtml", "OEBPS/text/part%202.xhtml", "application/xhtml+xml", CompressedLength(path, "OEBPS/text/part 2.xhtml")),
                new EpubResource("OEBPS/text/part1.xhtml", "OEBPS/text/part1.xhtml", "application/xhtml+xml", CompressedLength(path, "OEBPS/text/part1.xhtml")),
            ],
            publication.ReadingOrder);
        Assert.Equal(
            [
                new EpubResource("OEBPS/nav.xhtml", "OEBPS/nav.xhtml", "application/xhtml+xml", CompressedLength(path, "OEBPS/nav.xhtml")),
                new EpubResource("OEBPS/styles/book.css", "OEBPS/styles/book.css", "text/css", CompressedLength(path, "OEBPS/styles/book.css")),
                new EpubResource("OEBPS/images/plate.png", "OEBPS/images/plate.png", "image/png", CompressedLength(path, "OEBPS/images/plate.png")),
            ],
            publication.Resources.OrderBy(resource => resource.EntryName == "OEBPS/nav.xhtml" ? 0 : 1));
    }

    [Fact]
    public async Task Publication_with_no_title_has_none()
    {
        var path = await WriteAsync(new EpubBuilder().Build());

        var publication = await EpubPublicationReader.ReadAsync(path, CancellationToken);

        Assert.Null(publication.Title);
        Assert.Null(publication.Identifier);
        Assert.Null(publication.Language);
        Assert.Empty(publication.Authors);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Table_of_contents_keeps_nesting_and_fragments(int version)
    {
        var path = await WriteAsync(new EpubBuilder(version)
            .WithChapter("text/one.xhtml", "<h1 id=\"start\">One</h1><h2 id=\"s1\">Section</h2>")
            .WithChapter("text/two words.xhtml", "<h1>Two</h1>")
            .WithContents(
                new Contents("One", "text/one.xhtml#start",
                    new Contents("Section", "text/one.xhtml#s1")),
                new Contents("Two", "text/two%20words.xhtml"))
            .Build());

        var publication = await EpubPublicationReader.ReadAsync(path, CancellationToken);

        Assert.Equal(
            [
                new EpubTocEntry("One", "OEBPS/text/one.xhtml", "start",
                    [new EpubTocEntry("Section", "OEBPS/text/one.xhtml", "s1", [])]),
                new EpubTocEntry("Two", "OEBPS/text/two%20words.xhtml", null, []),
            ],
            publication.Toc,
            TocComparer.Instance);
    }

    [Fact]
    public async Task Epub_3_contents_are_not_wrapped_in_the_nav_heading()
    {
        var path = await WriteAsync(new EpubBuilder(version: 3)
            .WithChapter("one.xhtml", "<p>One</p>")
            .WithChapter("two.xhtml", "<p>Two</p>")
            .WithContents(
                new Contents("One", "one.xhtml"),
                new Contents("Part Two", null,
                    new Contents("Two", "two.xhtml")))
            .Build());

        var publication = await EpubPublicationReader.ReadAsync(path, CancellationToken);

        Assert.Equal(
            [
                new EpubTocEntry("One", "OEBPS/one.xhtml", null, []),
                new EpubTocEntry("Part Two", "OEBPS/two.xhtml", null,
                    [new EpubTocEntry("Two", "OEBPS/two.xhtml", null, [])]),
            ],
            publication.Toc,
            TocComparer.Instance);
    }

    [Fact]
    public async Task Epub_2_contents_come_from_the_ncx()
    {
        var path = await WriteAsync(new EpubBuilder(version: 2)
            .WithChapter("one.xhtml", "<p>One</p>")
            .WithContents(new Contents("The Only Chapter", "one.xhtml"))
            .Build());

        var publication = await EpubPublicationReader.ReadAsync(path, CancellationToken);

        Assert.Equal([new EpubTocEntry("The Only Chapter", "OEBPS/one.xhtml", null, [])], publication.Toc, TocComparer.Instance);
        Assert.Contains(publication.Resources, resource => resource is { EntryName: "OEBPS/toc.ncx", MediaType: "application/x-dtbncx+xml" });
    }

    [Fact]
    public async Task Drm_encrypted_epub_is_unreadable()
    {
        var path = await WriteAsync(new EpubBuilder()
            .WithTitle("Book")
            .WithEncryption(EpubBuilder.Aes128Cbc)
            .Build());

        await Assert.ThrowsAsync<UnreadableBookException>(() => EpubPublicationReader.ReadAsync(path, CancellationToken));
    }

    [Fact]
    public async Task Manifest_item_missing_from_the_archive_is_left_out()
    {
        var path = await WriteAsync(new EpubBuilder()
            .WithManifestItem("styles/book.css", "p { }", "text/css")
            .WithoutFile("OEBPS/styles/book.css")
            .Build());

        var publication = await EpubPublicationReader.ReadAsync(path, CancellationToken);

        Assert.DoesNotContain(publication.Resources, resource => resource.EntryName == "OEBPS/styles/book.css");
    }

    /// <summary>Compares table-of-contents entries by value, children included, since records compare lists by reference.</summary>
    private sealed class TocComparer : IEqualityComparer<EpubTocEntry>
    {
        public static readonly TocComparer Instance = new();

        public bool Equals(EpubTocEntry? x, EpubTocEntry? y) =>
            x is not null && y is not null
            && x.Title == y.Title && x.Href == y.Href && x.Fragment == y.Fragment
            && x.Children.SequenceEqual(y.Children, this);

        public int GetHashCode(EpubTocEntry entry) => HashCode.Combine(entry.Title, entry.Href, entry.Fragment);
    }

    private static long CompressedLength(string path, string entryName)
    {
        using var archive = ZipFile.OpenRead(path);
        return archive.GetEntry(entryName)!.CompressedLength;
    }

    private async Task<string> WriteAsync(byte[] bytes)
    {
        var path = Path.Combine(_folder, "book.epub");
        await File.WriteAllBytesAsync(path, bytes, CancellationToken);
        return path;
    }
}
