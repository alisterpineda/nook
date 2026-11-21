using Nook.Api.Data;
using Nook.Api.Ingest;
using Nook.Api.Ingest.Epub;

namespace Nook.Api.Tests.Integration;

/// <summary>Reads the real EPUBs in <c>tests/fixtures/epubs</c>, as their producers ship them.</summary>
public sealed class RealEpubTests
{
    private const string PrincessOfMars = "standard-ebooks-a-princess-of-mars.epub";
    private const string YellowWallpaper = "gutenberg-the-yellow-wallpaper-epub2.epub";

    private readonly IFormatHandler _handler = new EpubFormatHandler();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Epubs", name);

    [Theory]
    [InlineData(PrincessOfMars)]
    [InlineData(YellowWallpaper)]
    public async Task Real_epub_is_recognised(string name)
    {
        Assert.True(await _handler.MatchesAsync(Fixture(name), CancellationToken));
    }

    [Fact]
    public async Task Metadata_is_read_from_a_standard_ebooks_epub_3()
    {
        var metadata = await _handler.ReadMetadataAsync(Fixture(PrincessOfMars), CancellationToken);

        Assert.Equal("A Princess of Mars", metadata.Title);
        Assert.Equal("Princess of Mars, A", metadata.SortTitle);
        Assert.Equal(
            [new BookCreator("Edgar Rice Burroughs", ContributorRole.Author, "Burroughs, Edgar Rice")],
            metadata.Creators);
        Assert.Equal(
            [
                "Carter, John (Fictitious character)--Fiction",
                "Dejah Thoris (Fictitious character)--Fiction",
                "Princesses--Fiction",
                "Mars (Planet)--Fiction",
            ],
            metadata.Subjects);
        Assert.StartsWith("<p>In <i>A Princess of Mars</i>, John Carter", metadata.Description);
        Assert.Equal("Standard Ebooks", metadata.Publisher);
        Assert.Equal("en-US", metadata.Language);
        Assert.Equal(new DateOnly(2014, 5, 25), metadata.PublicationDate);
        Assert.Equal(new BookSeries("Martian", 1), metadata.Series);
        Assert.Empty(metadata.Identifiers);
    }

    [Fact]
    public async Task Metadata_is_read_from_a_project_gutenberg_epub_2()
    {
        var metadata = await _handler.ReadMetadataAsync(Fixture(YellowWallpaper), CancellationToken);

        Assert.Equal("The Yellow Wallpaper", metadata.Title);
        Assert.Null(metadata.SortTitle);
        Assert.Equal(
            [new BookCreator("Charlotte Perkins Gilman", ContributorRole.Author, "Gilman, Charlotte Perkins")],
            metadata.Creators);
        Assert.Equal(
            [
                "Psychological fiction",
                "Mentally ill women -- Fiction",
                "Feminist fiction",
                "Married women -- Psychology -- Fiction",
                "Sex role -- Fiction",
            ],
            metadata.Subjects);
        Assert.Null(metadata.Description);
        Assert.Null(metadata.Publisher);
        Assert.Equal("en", metadata.Language);
        Assert.Equal(new DateOnly(1999, 11, 1), metadata.PublicationDate);
        Assert.Null(metadata.Series);
        Assert.Equal([new BookIdentifier("uri", "http://www.gutenberg.org/1952")], metadata.Identifiers);
    }

    [Theory]
    [InlineData(PrincessOfMars, 392677)]
    [InlineData(YellowWallpaper, 202358)]
    public async Task Cover_is_read_from_a_real_epub(string name, int length)
    {
        var cover = await _handler.ReadCoverAsync(Fixture(name), CancellationToken);

        Assert.NotNull(cover);
        Assert.Equal(".jpg", cover.Extension);
        Assert.Equal(length, cover.Bytes.Length);
        Assert.Equal([0xFF, 0xD8, 0xFF], cover.Bytes[..3]);
    }

    [Fact]
    public async Task Publication_is_read_from_a_standard_ebooks_epub_3()
    {
        var publication = await EpubPublicationReader.ReadAsync(Fixture(PrincessOfMars), CancellationToken);

        Assert.Equal("https://standardebooks.org/ebooks/edgar-rice-burroughs/a-princess-of-mars", publication.Identifier);
        Assert.Equal(["Edgar Rice Burroughs"], publication.Authors);

        Assert.Equal(36, publication.ReadingOrder.Count);
        Assert.Equal("epub/text/titlepage.xhtml", publication.ReadingOrder[0].EntryName);
        Assert.Equal("epub/text/chapter-1.xhtml", publication.ReadingOrder[5].EntryName);
        Assert.Equal("epub/text/uncopyright.xhtml", publication.ReadingOrder[^1].EntryName);

        Assert.Equal(
            ["Titlepage", "Imprint", "Dedication", "Foreword", "A Princess of Mars", "Endnotes", "Colophon", "Uncopyright"],
            publication.Toc.Select(entry => entry.Title));
        var chapters = publication.Toc[4].Children;
        Assert.Equal(28, chapters.Count);
        Assert.Equal(("I: On the Arizona Hills", "epub/text/chapter-1.xhtml", null), (chapters[0].Title, chapters[0].Href, chapters[0].Fragment));
        Assert.Empty(chapters[0].Children);
    }

    [Fact]
    public async Task Publication_is_read_from_a_project_gutenberg_epub_2()
    {
        var publication = await EpubPublicationReader.ReadAsync(Fixture(YellowWallpaper), CancellationToken);

        Assert.Equal("The Yellow Wallpaper", publication.Title);
        Assert.Equal("http://www.gutenberg.org/1952", publication.Identifier);
        Assert.Equal(["Charlotte Perkins Gilman"], publication.Authors);

        Assert.Equal(
            [
                "OEBPS/wrap0000.html",
                "OEBPS/8878401390937179042_1952-h-0.htm.html",
                "OEBPS/8878401390937179042_1952-h-1.htm.html",
            ],
            publication.ReadingOrder.Select(resource => resource.EntryName));

        Assert.Equal(
            [
                ("The Yellow Wallpaper", "OEBPS/8878401390937179042_1952-h-0.htm.html", "pgepubid00000"),
                ("THE FULL PROJECT GUTENBERG™ LICENSE", "OEBPS/8878401390937179042_1952-h-1.htm.html", "pg-footer-heading"),
            ],
            publication.Toc.Select(entry => (entry.Title, entry.Href, entry.Fragment)));
    }
}
