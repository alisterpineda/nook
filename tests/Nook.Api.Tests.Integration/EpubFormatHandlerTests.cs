using System.IO.Compression;
using System.Text;
using Nook.Api.Data;
using Nook.Api.Ingest;
using Nook.Api.Ingest.Epub;

namespace Nook.Api.Tests.Integration;

public sealed class EpubFormatHandlerTests : IDisposable
{
    private static readonly byte[] JpegCover = [0xFF, 0xD8, 0xFF, 0xE0, 5, 6, 7, 8];

    private readonly IFormatHandler _handler = new EpubFormatHandler();

    private readonly string _folder = Directory.CreateTempSubdirectory("nook-epub-tests-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void Handler_describes_the_epub_format()
    {
        Assert.Equal(ItemFormat.Epub, _handler.Format);
        Assert.Equal("application/epub+zip", _handler.MediaType);
        Assert.Equal(".epub", _handler.Extension);
    }

    [Theory]
    [InlineData("book.epub")]
    [InlineData("book.txt")]
    [InlineData("book")]
    public async Task Epub_is_recognised_from_its_content_whatever_its_name(string fileName)
    {
        var path = await WriteAsync(new EpubBuilder().WithTitle("Book").Build(), fileName);

        Assert.True(await _handler.MatchesAsync(path, CancellationToken));
    }

    [Fact]
    public async Task Epub_without_a_mimetype_entry_is_recognised_by_its_container_file()
    {
        var path = await WriteAsync(new EpubBuilder().WithTitle("Book").WithoutFile("mimetype").Build());

        Assert.True(await _handler.MatchesAsync(path, CancellationToken));
    }

    public static TheoryData<string, byte[]> NotEpubs()
    {
        var epub = new EpubBuilder().WithTitle("Book").Build();
        return new()
        {
            { "text file", TestData.NotABook },
            { "empty file", [] },
            { "plain zip", Zip(("notes.txt", "Just some notes.")) },
            { "zip with another mimetype", Zip(("mimetype", "application/zip"), ("META-INF/container.xml", "<container/>")) },
            { "truncated epub", epub[..(epub.Length / 2)] },
        };
    }

    [Theory]
    [MemberData(nameof(NotEpubs))]
    public async Task File_that_is_not_an_epub_is_not_recognised(string description, byte[] bytes)
    {
        var path = await WriteAsync(bytes, "book.epub");

        Assert.False(await _handler.MatchesAsync(path, CancellationToken), description);
    }

    [Fact]
    public async Task Metadata_is_read_from_an_epub_2_file()
    {
        var path = await WriteAsync(new EpubBuilder(version: 2)
            .WithTitle("The Colour of Magic")
            .WithCreator("Terry Pratchett", role: "aut", fileAs: "Pratchett, Terry")
            .WithCreator("Josh Kirby", role: "ill")
            .WithSubject("Fantasy")
            .WithSubject("Humour")
            .WithDescription("A tourist comes to the Discworld.")
            .WithPublisher("Colin Smythe")
            .WithLanguage("en")
            .WithDate("2010-03-15T00:00:00+00:00", eventName: "modification")
            .WithDate("1983-11-24", eventName: "publication")
            .Build());

        var metadata = await _handler.ReadMetadataAsync(path, CancellationToken);

        Assert.Equal("The Colour of Magic", metadata.Title);
        Assert.Equal(["Terry Pratchett", "Josh Kirby"], metadata.Creators.Select(creator => creator.Name));
        Assert.Equal(["Fantasy", "Humour"], metadata.Subjects);
        Assert.Equal("A tourist comes to the Discworld.", metadata.Description);
        Assert.Equal("Colin Smythe", metadata.Publisher);
        Assert.Equal("en", metadata.Language);
        Assert.Equal(new DateOnly(1983, 11, 24), metadata.PublicationDate);
        Assert.Null(metadata.Series);
    }

    [Fact]
    public async Task Metadata_is_read_from_an_epub_3_file()
    {
        var path = await WriteAsync(new EpubBuilder(version: 3)
            .WithTitle("   ")
            .WithTitle("Piranesi")
            .WithTitle("A Novel")
            .WithCreator("Susanna Clarke", role: "aut", fileAs: "Clarke, Susanna")
            .WithCreator("Chiwetel Ejiofor", role: "nrt")
            .WithSubject("Fantasy")
            .WithDescription("Piranesi lives in the House.")
            .WithDescription("A second description.")
            .WithPublisher("Bloomsbury")
            .WithPublisher("Another Publisher")
            .WithLanguage("en-GB")
            .WithLanguage("fr")
            .WithDate("2020-09-15T00:00:00+00:00")
            .Build());

        var metadata = await _handler.ReadMetadataAsync(path, CancellationToken);

        Assert.Equal("Piranesi", metadata.Title);
        Assert.Equal(["Susanna Clarke", "Chiwetel Ejiofor"], metadata.Creators.Select(creator => creator.Name));
        Assert.Equal(["Fantasy"], metadata.Subjects);
        Assert.Equal("Piranesi lives in the House.", metadata.Description);
        Assert.Equal("Bloomsbury", metadata.Publisher);
        Assert.Equal("en-GB", metadata.Language);
        Assert.Equal(new DateOnly(2020, 9, 15), metadata.PublicationDate);
    }

    [Fact]
    public async Task Sort_values_and_roles_are_read_from_an_epub_2_file()
    {
        var path = await WriteAsync(new EpubBuilder(version: 2)
            .WithTitle("The Left Hand of Darkness")
            .WithMeta("calibre:title_sort", "Left Hand of Darkness, The")
            .WithCreator("Ursula K. Le Guin", role: "aut", fileAs: "Le Guin, Ursula K.")
            .WithCreator("Jane Translator", role: " TRL ", fileAs: "   ")
            .WithCreator("No Role")
            .Build());

        var metadata = await _handler.ReadMetadataAsync(path, CancellationToken);

        Assert.Equal("Left Hand of Darkness, The", metadata.SortTitle);
        Assert.Equal(
            [
                new BookCreator("Ursula K. Le Guin", "aut", "Le Guin, Ursula K."),
                new BookCreator("Jane Translator", "TRL", null),
                new BookCreator("No Role", null, null),
            ],
            metadata.Creators);
    }

    [Fact]
    public async Task Sort_values_and_roles_are_read_from_an_epub_3_file()
    {
        var path = await WriteAsync(new EpubBuilder(version: 3)
            .WithTitle("The Dispossessed", id: "main-title")
            .WithProperty("file-as", "Dispossessed, The", refines: "main-title")
            .WithCreator("Ursula K. Le Guin", role: "aut", fileAs: "Le Guin, Ursula K.")
            .WithCreator("Someone Reading", role: "nrt")
            .WithCreator("No Role", fileAs: "Role, No")
            .Build());

        var metadata = await _handler.ReadMetadataAsync(path, CancellationToken);

        Assert.Equal("Dispossessed, The", metadata.SortTitle);
        Assert.Equal(
            [
                new BookCreator("Ursula K. Le Guin", "aut", "Le Guin, Ursula K."),
                new BookCreator("Someone Reading", "nrt", null),
                new BookCreator("No Role", null, "Role, No"),
            ],
            metadata.Creators);
    }

    [Fact]
    public async Task Calibre_title_sort_is_preferred_to_a_file_as_refining_the_title()
    {
        var path = await WriteAsync(new EpubBuilder(version: 3)
            .WithTitle("The Dispossessed", id: "main-title")
            .WithProperty("file-as", "Refined, The", refines: "main-title")
            .WithMeta("calibre:title_sort", "Dispossessed, The")
            .Build());

        var metadata = await _handler.ReadMetadataAsync(path, CancellationToken);

        Assert.Equal("Dispossessed, The", metadata.SortTitle);
    }

    [Fact]
    public async Task File_as_refining_a_title_other_than_the_one_read_is_not_its_sort_title()
    {
        var path = await WriteAsync(new EpubBuilder(version: 3)
            .WithTitle("The Dispossessed", id: "main-title")
            .WithTitle("An Ambiguous Utopia", id: "subtitle")
            .WithProperty("file-as", "Ambiguous Utopia, An", refines: "subtitle")
            .Build());

        var metadata = await _handler.ReadMetadataAsync(path, CancellationToken);

        Assert.Equal("The Dispossessed", metadata.Title);
        Assert.Null(metadata.SortTitle);
    }

    [Fact]
    public async Task Epub_with_no_metadata_reads_as_empty()
    {
        var path = await WriteAsync(new EpubBuilder().Build());

        var metadata = await _handler.ReadMetadataAsync(path, CancellationToken);

        Assert.Null(metadata.Title);
        Assert.Null(metadata.SortTitle);
        Assert.Empty(metadata.Creators);
        Assert.Empty(metadata.Subjects);
        Assert.Null(metadata.Description);
        Assert.Null(metadata.Publisher);
        Assert.Null(metadata.Language);
        Assert.Null(metadata.PublicationDate);
        Assert.Null(metadata.Series);
    }

    [Theory]
    [InlineData("1983")]
    [InlineData("1983-11")]
    [InlineData("not a date")]
    public async Task Publication_date_that_is_not_a_complete_date_is_left_empty(string date)
    {
        var path = await WriteAsync(new EpubBuilder().WithTitle("Book").WithDate(date).Build());

        var metadata = await _handler.ReadMetadataAsync(path, CancellationToken);

        Assert.Null(metadata.PublicationDate);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Identifiers_are_read_with_their_scheme_and_without_a_prefix_naming_it(int version)
    {
        var path = await WriteAsync(new EpubBuilder(version)
            .WithIdentifier("12345", scheme: "Goodreads")
            .WithIdentifier("urn:isbn:978-0-441-47812-5", scheme: "ISBN")
            .WithIdentifier("URN:UUID:0f3b6a6e-1c2d-4e5f-8a9b-0c1d2e3f4a5b")
            .WithIdentifier("calibre:42")
            .WithIdentifier("9d2c4e1a-3b5f-4a7c-8e9d-0a1b2c3d4e5f")
            .WithIdentifier("https://example.com/books/1")
            .WithIdentifier("   ", scheme: "isbn")
            .Build());

        var metadata = await _handler.ReadMetadataAsync(path, CancellationToken);

        Assert.Equal(
            [
                new BookIdentifier("goodreads", "12345"),
                new BookIdentifier("isbn", "978-0-441-47812-5"),
                new BookIdentifier("uuid", "0f3b6a6e-1c2d-4e5f-8a9b-0c1d2e3f4a5b"),
                new BookIdentifier("calibre", "42"),
            ],
            metadata.Identifiers);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Series_is_read_from_calibre_tags(int version)
    {
        var path = await WriteAsync(new EpubBuilder(version)
            .WithTitle("The Light Fantastic")
            .WithCalibreSeries("Discworld", "2.5")
            .WithCollection("Another Series", "7")
            .Build());

        var metadata = await _handler.ReadMetadataAsync(path, CancellationToken);

        Assert.Equal(new BookSeries("Discworld", 2.5m), metadata.Series);
    }

    [Fact]
    public async Task Series_is_read_from_an_epub_3_collection()
    {
        var path = await WriteAsync(new EpubBuilder(version: 3)
            .WithTitle("The Fifth Season")
            .WithCollection("The Broken Earth", "1")
            .Build());

        var metadata = await _handler.ReadMetadataAsync(path, CancellationToken);

        Assert.Equal(new BookSeries("The Broken Earth", 1m), metadata.Series);
    }

    [Fact]
    public async Task Series_position_that_is_not_a_number_is_left_empty()
    {
        var calibre = await WriteAsync(new EpubBuilder(version: 2)
            .WithTitle("Book").WithCalibreSeries("Discworld", "first").Build(), "calibre.epub");
        var collection = await WriteAsync(new EpubBuilder(version: 3)
            .WithTitle("Book").WithCollection("The Broken Earth", "one").Build(), "collection.epub");
        var unpositioned = await WriteAsync(new EpubBuilder(version: 3)
            .WithTitle("Book").WithCollection("The Broken Earth").Build(), "unpositioned.epub");

        Assert.Equal(new BookSeries("Discworld", null), (await _handler.ReadMetadataAsync(calibre, CancellationToken)).Series);
        Assert.Equal(new BookSeries("The Broken Earth", null), (await _handler.ReadMetadataAsync(collection, CancellationToken)).Series);
        Assert.Equal(new BookSeries("The Broken Earth", null), (await _handler.ReadMetadataAsync(unpositioned, CancellationToken)).Series);
    }

    [Fact]
    public async Task Cover_is_read_from_an_epub_2_file()
    {
        var path = await WriteAsync(new EpubBuilder(version: 2)
            .WithTitle("Book")
            .WithCover(TestData.PngCover, "Cover.PNG", "image/png")
            .Build());

        var cover = await _handler.ReadCoverAsync(path, CancellationToken);

        Assert.NotNull(cover);
        Assert.Equal(TestData.PngCover, cover.Bytes);
        Assert.Equal(".png", cover.Extension);
    }

    [Fact]
    public async Task Cover_is_read_from_an_epub_3_file()
    {
        var path = await WriteAsync(new EpubBuilder(version: 3)
            .WithTitle("Book")
            .WithCover(JpegCover, "front.jpeg", "image/jpeg")
            .Build());

        var cover = await _handler.ReadCoverAsync(path, CancellationToken);

        Assert.NotNull(cover);
        Assert.Equal(JpegCover, cover.Bytes);
        Assert.Equal(".jpeg", cover.Extension);
    }

    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    public async Task Cover_without_an_extension_takes_one_from_its_media_type(string mediaType, string extension)
    {
        var path = await WriteAsync(new EpubBuilder()
            .WithTitle("Book")
            .WithCover(TestData.PngCover, "cover", mediaType)
            .Build());

        var cover = await _handler.ReadCoverAsync(path, CancellationToken);

        Assert.NotNull(cover);
        Assert.Equal(extension, cover.Extension);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Epub_without_a_cover_has_none(int version)
    {
        var path = await WriteAsync(new EpubBuilder(version).WithTitle("Book").Build());

        Assert.Null(await _handler.ReadCoverAsync(path, CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Drm_encrypted_epub_is_unreadable(bool alsoObfuscatesFonts)
    {
        string[] algorithms = alsoObfuscatesFonts
            ? [EpubBuilder.IdpfFontObfuscation, EpubBuilder.Aes128Cbc]
            : [EpubBuilder.Aes128Cbc];
        var path = await WriteAsync(new EpubBuilder()
            .WithTitle("Book")
            .WithCover(TestData.PngCover)
            .WithEncryption(algorithms)
            .Build());

        Assert.True(await _handler.MatchesAsync(path, CancellationToken));
        await Assert.ThrowsAsync<UnreadableBookException>(() => _handler.ReadMetadataAsync(path, CancellationToken));
        await Assert.ThrowsAsync<UnreadableBookException>(() => _handler.ReadCoverAsync(path, CancellationToken));
    }

    [Fact]
    public async Task Epub_with_only_obfuscated_fonts_is_readable()
    {
        var path = await WriteAsync(new EpubBuilder()
            .WithTitle("Book")
            .WithCover(TestData.PngCover)
            .WithEncryption(EpubBuilder.IdpfFontObfuscation, EpubBuilder.AdobeFontObfuscation)
            .Build());

        Assert.Equal("Book", (await _handler.ReadMetadataAsync(path, CancellationToken)).Title);
        Assert.Equal(TestData.PngCover, (await _handler.ReadCoverAsync(path, CancellationToken))?.Bytes);
    }

    [Theory]
    [InlineData("package file that is not well-formed")]
    [InlineData("package file missing")]
    [InlineData("container file that is not well-formed")]
    public async Task Epub_with_a_broken_package_or_container_file_is_unreadable(string breakage)
    {
        var builder = new EpubBuilder().WithTitle("Book");
        builder = breakage switch
        {
            "package file that is not well-formed" => builder.WithFile("OEBPS/content.opf", "<package><metadata>"),
            "package file missing" => builder.WithoutFile("OEBPS/content.opf"),
            "container file that is not well-formed" => builder.WithFile("META-INF/container.xml", "<container"),
            _ => throw new ArgumentOutOfRangeException(nameof(breakage), breakage, null),
        };
        var path = await WriteAsync(builder.Build());

        Assert.True(await _handler.MatchesAsync(path, CancellationToken));
        await Assert.ThrowsAsync<UnreadableBookException>(() => _handler.ReadMetadataAsync(path, CancellationToken));
        await Assert.ThrowsAsync<UnreadableBookException>(() => _handler.ReadCoverAsync(path, CancellationToken));
    }

    private async Task<string> WriteAsync(byte[] bytes, string fileName = "book.epub")
    {
        var path = Path.Combine(_folder, fileName);
        await File.WriteAllBytesAsync(path, bytes, CancellationToken);
        return path;
    }

    private static byte[] Zip(params (string Path, string Content)[] files) =>
        EpubBuilder.Zip(files.Select(file => (file.Path, Encoding.UTF8.GetBytes(file.Content))));
}
