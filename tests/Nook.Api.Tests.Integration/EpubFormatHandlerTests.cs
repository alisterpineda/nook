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
            .WithCreator("Jane Translator", role: "trl", fileAs: "   ")
            .WithCreator("Some Illustrator", role: "ill")
            .WithCreator("No Role")
            .Build());

        var metadata = await _handler.ReadMetadataAsync(path, CancellationToken);

        Assert.Equal("Left Hand of Darkness, The", metadata.SortTitle);
        Assert.Equal(
            [
                new BookCreator("Ursula K. Le Guin", ContributorRole.Author, "Le Guin, Ursula K."),
                new BookCreator("Jane Translator", ContributorRole.Translator, null),
                new BookCreator("Some Illustrator", null, null),
                new BookCreator("No Role", ContributorRole.Author, null),
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
            .WithCreator("Some Illustrator", role: "ill")
            .WithCreator("No Role", fileAs: "Role, No")
            .Build());

        var metadata = await _handler.ReadMetadataAsync(path, CancellationToken);

        Assert.Equal("Dispossessed, The", metadata.SortTitle);
        Assert.Equal(
            [
                new BookCreator("Ursula K. Le Guin", ContributorRole.Author, "Le Guin, Ursula K."),
                new BookCreator("Someone Reading", ContributorRole.Narrator, null),
                new BookCreator("Some Illustrator", null, null),
                new BookCreator("No Role", ContributorRole.Author, "Role, No"),
            ],
            metadata.Creators);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Role_code_is_read_ignoring_case_and_spaces(int version)
    {
        var path = await WriteAsync(new EpubBuilder(version)
            .WithCreator("Jane Translator", role: " TRL ")
            .WithCreator("Ed Itor", role: "Edt")
            .Build());

        var metadata = await _handler.ReadMetadataAsync(path, CancellationToken);

        Assert.Equal(
            [ContributorRole.Translator, ContributorRole.Editor],
            metadata.Creators.Select(creator => creator.Role));
    }

    [Fact]
    public async Task First_role_nook_keeps_is_read_from_a_creator_with_several()
    {
        var path = await WriteAsync(new EpubBuilder(version: 3)
            .WithCreatorRoles("Edgar Rice Burroughs", "ann", "aut", "dto")
            .WithCreatorRoles("Ed Itor", "edt", "aut")
            .WithCreatorRoles("Some Illustrator", "ill", "art")
            .WithCreatorRoles("Blank Roles", " ", "")
            .Build());

        var metadata = await _handler.ReadMetadataAsync(path, CancellationToken);

        Assert.Equal(
            [ContributorRole.Author, ContributorRole.Editor, null, ContributorRole.Author],
            metadata.Creators.Select(creator => creator.Role));
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
    [InlineData("0101-01-01T00:00:00+00:00")]
    [InlineData("0101-01-01")]
    public async Task Calibre_undefined_date_is_left_empty(string date)
    {
        var path = await WriteAsync(new EpubBuilder().WithTitle("Book").WithDate(date).Build());

        var metadata = await _handler.ReadMetadataAsync(path, CancellationToken);

        Assert.Null(metadata.PublicationDate);
    }

    [Fact]
    public async Task Early_real_date_is_kept()
    {
        var path = await WriteAsync(new EpubBuilder().WithTitle("Book").WithDate("0868-05-11").Build());

        var metadata = await _handler.ReadMetadataAsync(path, CancellationToken);

        Assert.Equal(new DateOnly(868, 5, 11), metadata.PublicationDate);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Identifier_scheme_is_lowercased_whatever_its_case_in_the_file(int version)
    {
        var path = await WriteAsync(new EpubBuilder(version)
            .WithIdentifier("9780441478125", scheme: "ISBN")
            .WithIdentifier("URN:ISBN:0441478123")
            .Build());

        var metadata = await _handler.ReadMetadataAsync(path, CancellationToken);

        Assert.Equal(
            [new BookIdentifier("isbn", "9780441478125"), new BookIdentifier("isbn", "0441478123")],
            metadata.Identifiers);
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
    public async Task Collection_typed_as_a_set_is_not_a_series()
    {
        var path = await WriteAsync(new EpubBuilder(version: 3)
            .WithTitle("The Fifth Season")
            .WithCollection("Hugo Award Winners", "3", type: "set")
            .Build());

        var metadata = await _handler.ReadMetadataAsync(path, CancellationToken);

        Assert.Null(metadata.Series);
    }

    [Fact]
    public async Task Collection_typed_as_a_series_is_one()
    {
        var path = await WriteAsync(new EpubBuilder(version: 3)
            .WithTitle("The Fifth Season")
            .WithCollection("The Broken Earth", "1", type: " Series ")
            .Build());

        var metadata = await _handler.ReadMetadataAsync(path, CancellationToken);

        Assert.Equal(new BookSeries("The Broken Earth", 1m), metadata.Series);
    }

    [Fact]
    public async Task Series_collection_is_found_after_a_set()
    {
        var path = await WriteAsync(new EpubBuilder(version: 3)
            .WithTitle("The Fifth Season")
            .WithCollection("Hugo Award Winners", "3", type: "set")
            .WithCollection("The Broken Earth", "1", type: "series")
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

    [Fact]
    public async Task Cover_named_like_a_book_takes_its_extension_from_its_media_type()
    {
        var path = await WriteAsync(new EpubBuilder()
            .WithTitle("Book")
            .WithCover(TestData.PngCover, "x.epub", "image/png")
            .Build());

        var cover = await _handler.ReadCoverAsync(path, CancellationToken);

        Assert.NotNull(cover);
        Assert.Equal(".png", cover.Extension);
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

    [Fact]
    public async Task Cover_over_the_size_cap_reads_as_no_cover()
    {
        // Zero bytes compress to almost nothing, so the archive stays small while the cover inflates past the cap.
        var path = await WriteAsync(new EpubBuilder()
            .WithTitle("Book")
            .WithCover(new byte[21 * Mebibyte])
            .Build());

        Assert.Null(await _handler.ReadCoverAsync(path, CancellationToken));
        Assert.Equal("Book", (await _handler.ReadMetadataAsync(path, CancellationToken)).Title);
    }

    [Fact]
    public async Task Cover_at_the_size_cap_is_read()
    {
        var path = await WriteAsync(new EpubBuilder()
            .WithTitle("Book")
            .WithCover(new byte[20 * Mebibyte])
            .Build());

        var cover = await _handler.ReadCoverAsync(path, CancellationToken);

        Assert.NotNull(cover);
        Assert.Equal(20 * Mebibyte, cover.Bytes.Length);
        Assert.True(cover.Bytes.AsSpan().IndexOfAnyExcept((byte)0) < 0, "Expected the cover's bytes as written.");
    }

    [Fact]
    public async Task Oversized_encryption_file_is_unreadable()
    {
        // Only font obfuscation, which is not DRM: the size alone makes the book unreadable.
        var path = await WriteAsync(new EpubBuilder()
            .WithTitle("Book")
            .WithFile("META-INF/encryption.xml", $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <encryption xmlns="urn:oasis:names:tc:opendocument:xmlns:container" xmlns:enc="http://www.w3.org/2001/04/xmlenc#">
                  <!--{Padding(Mebibyte)}-->
                  <enc:EncryptedData>
                    <enc:EncryptionMethod Algorithm="{EpubBuilder.IdpfFontObfuscation}"/>
                    <enc:CipherData><enc:CipherReference URI="OEBPS/chapter.xhtml"/></enc:CipherData>
                  </enc:EncryptedData>
                </encryption>
                """)
            .Build());

        await Assert.ThrowsAsync<UnreadableBookException>(() => _handler.ReadMetadataAsync(path, CancellationToken));
        await Assert.ThrowsAsync<UnreadableBookException>(() => _handler.ReadCoverAsync(path, CancellationToken));
    }

    [Fact]
    public async Task Oversized_container_file_is_unreadable()
    {
        var path = await WriteAsync(PadEntry(new EpubBuilder().WithTitle("Book"), "META-INF/container.xml", Mebibyte));

        await Assert.ThrowsAsync<UnreadableBookException>(() => _handler.ReadMetadataAsync(path, CancellationToken));
        await Assert.ThrowsAsync<UnreadableBookException>(() => _handler.ReadCoverAsync(path, CancellationToken));
    }

    [Fact]
    public async Task Oversized_package_file_is_unreadable()
    {
        var path = await WriteAsync(PadEntry(new EpubBuilder().WithTitle("Book"), "OEBPS/content.opf", 10 * Mebibyte));

        await Assert.ThrowsAsync<UnreadableBookException>(() => _handler.ReadMetadataAsync(path, CancellationToken));
        await Assert.ThrowsAsync<UnreadableBookException>(() => _handler.ReadCoverAsync(path, CancellationToken));
    }

    [Fact]
    public async Task Oversized_package_file_under_another_extension_is_unreadable()
    {
        // The container names the package file, so its extension is the book's to choose.
        var builder = new EpubBuilder().WithTitle("Book");
        string package, container;
        using (var archive = new ZipArchive(new MemoryStream(builder.Build()), ZipArchiveMode.Read))
        {
            using var packageReader = new StreamReader(archive.GetEntry("OEBPS/content.opf")!.Open(), Encoding.UTF8);
            using var containerReader = new StreamReader(archive.GetEntry("META-INF/container.xml")!.Open(), Encoding.UTF8);
            package = packageReader.ReadToEnd();
            container = containerReader.ReadToEnd();
        }

        var path = await WriteAsync(builder
            .WithFile("META-INF/container.xml", container.Replace("content.opf", "content.pkg"))
            .WithFile("OEBPS/content.pkg", $"{package}\n<!--{Padding(10 * Mebibyte)}-->\n")
            .Build());

        await Assert.ThrowsAsync<UnreadableBookException>(() => _handler.ReadMetadataAsync(path, CancellationToken));
    }

    [Fact]
    public async Task Package_file_under_another_extension_is_read()
    {
        var builder = new EpubBuilder().WithTitle("Book");
        string package, container;
        using (var archive = new ZipArchive(new MemoryStream(builder.Build()), ZipArchiveMode.Read))
        {
            using var packageReader = new StreamReader(archive.GetEntry("OEBPS/content.opf")!.Open(), Encoding.UTF8);
            using var containerReader = new StreamReader(archive.GetEntry("META-INF/container.xml")!.Open(), Encoding.UTF8);
            package = packageReader.ReadToEnd();
            container = containerReader.ReadToEnd();
        }

        var path = await WriteAsync(builder
            .WithFile("META-INF/container.xml", container.Replace("content.opf", "content.pkg"))
            .WithFile("OEBPS/content.pkg", package)
            .Build());

        Assert.Equal("Book", (await _handler.ReadMetadataAsync(path, CancellationToken)).Title);
    }

    [Theory]
    [InlineData(2, "OEBPS/toc.ncx")]
    [InlineData(3, "OEBPS/nav.xhtml")]
    public async Task Oversized_navigation_file_is_unreadable(int version, string entry)
    {
        var path = await WriteAsync(PadEntry(new EpubBuilder(version).WithTitle("Book"), entry, 10 * Mebibyte));

        await Assert.ThrowsAsync<UnreadableBookException>(() => _handler.ReadMetadataAsync(path, CancellationToken));
    }

    private const int Mebibyte = 1024 * 1024;

    /// <summary>The builder's EPUB with an XML comment appended to one of its entries, taking it past the size given.</summary>
    private static byte[] PadEntry(EpubBuilder builder, string entry, int size)
    {
        using var archive = new ZipArchive(new MemoryStream(builder.Build()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry(entry)!.Open(), Encoding.UTF8);
        var content = reader.ReadToEnd();
        return builder.WithFile(entry, $"{content}\n<!--{Padding(size)}-->\n").Build();
    }

    /// <summary>Spaces enough on their own to exceed the size given, which compress to almost nothing.</summary>
    private static string Padding(int size) => new(' ', size + 1);

    private async Task<string> WriteAsync(byte[] bytes, string fileName = "book.epub")
    {
        var path = Path.Combine(_folder, fileName);
        await File.WriteAllBytesAsync(path, bytes, CancellationToken);
        return path;
    }

    private static byte[] Zip(params (string Path, string Content)[] files) =>
        EpubBuilder.Zip(files.Select(file => (file.Path, Encoding.UTF8.GetBytes(file.Content))));
}
