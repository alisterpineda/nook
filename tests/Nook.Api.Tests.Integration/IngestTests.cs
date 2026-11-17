using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features;
using Nook.Api.Features.Jobs;
using Nook.Api.Features.Works;
using Nook.Api.Ingest;

namespace Nook.Api.Tests.Integration;

public class IngestTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Uploaded_epub_becomes_a_work_with_one_item_and_one_part()
    {
        var section = await factory.AddSectionAsync();
        var epub = new EpubBuilder()
            .WithTitle("Ancillary Justice")
            .WithCreator("Ann Leckie")
            .WithCreator("Second Writer")
            .WithCalibreSeries("Imperial Radch", "1")
            .WithSubject("Space Opera")
            .WithSubject("Fiction")
            .WithDescription("A ship in a body.")
            .WithPublisher("Orbit")
            .WithLanguage("en")
            .WithDate("2013-10-01")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "ancillary.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(IngestTestExtensions.StepNames, job.Steps.Select(s => s.Name));
        Assert.All(job.Steps, s => Assert.Equal(JobStepStatus.Succeeded, s.Status));
        var result = job.ReadIngestResult();
        Assert.Equal(IngestOutcome.Created, result.Outcome);

        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal("Ancillary Justice", work.Title);
        Assert.Equal("Ancillary Justice", work.SortTitle);
        Assert.Equal("A ship in a body.", work.Description);
        Assert.Equal("Orbit", work.Publisher);
        Assert.Equal("en", work.Language);
        Assert.Equal(new DateOnly(2013, 10, 1), work.PublicationDate);
        Assert.Equal(
            [("Ann Leckie", ContributorRole.Author), ("Second Writer", ContributorRole.Author)],
            work.Contributors.Select(c => (c.Name, c.Role)));
        Assert.Equal(("Imperial Radch", 1m), (work.Series?.Name, work.Series?.Position));
        Assert.Equal(["Fiction", "Space Opera"], work.Tags.Select(t => t.Name));
        var item = Assert.Single(work.Items);
        Assert.Equal(result.ItemId, item.Id);
        Assert.Equal(ItemFormat.Epub, item.Format);
        var part = Assert.Single(item.Parts);
        Assert.Equal("application/epub+zip", part.MediaType);
        Assert.Equal(epub.Length, part.Size);
    }

    [Fact]
    public async Task Html_in_an_epub_description_is_stored_as_plain_text()
    {
        var section = await factory.AddSectionAsync();
        var epub = new EpubBuilder()
            .WithTitle("Come as You Are")
            .WithDescription(
                "<b>How it works&#8212;based on research.</b><BR>Researchers explain <i>Come as You Are</i>.<BR> <BR>The first lesson.")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "come-as-you-are.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal(
            "How it works—based on research.\nResearchers explain Come as You Are.\n\nThe first lesson.",
            work.Description);
    }

    [Fact]
    public async Task Work_shows_the_epub_title_contributors_series_tags_and_external_ids()
    {
        var section = await factory.AddSectionAsync();
        var author = $"Ursula K. Le Guin {IngestTestExtensions.UniqueSuffix()}";
        var epub = new EpubBuilder()
            .WithIdentifier("urn:uuid:5a1d3c8e-7f2b-4e6a-9c0d-1b2e3f4a5b6c")
            .WithIdentifier("9780441478125", scheme: "ISBN")
            .WithTitle("The Left Hand of Darkness")
            .WithCreator(author)
            .WithCalibreSeries("Hainish Cycle", "4")
            .WithSubject("Science Fiction")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "left-hand.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal("The Left Hand of Darkness", work.Title);
        Assert.Equal(
            [(author, ContributorRole.Author)],
            work.Contributors.Select(c => (c.Name, c.Role)));
        Assert.Equal(("Hainish Cycle", 4m), (work.Series?.Name, work.Series?.Position));
        Assert.Equal(["Science Fiction"], work.Tags.Select(t => t.Name));
        Assert.Equal(
            [("isbn", "9780441478125"), ("uuid", "5a1d3c8e-7f2b-4e6a-9c0d-1b2e3f4a5b6c")],
            work.ExternalIds.Select(e => (e.Scheme, e.Value)));
        var item = Assert.Single(work.Items);
        Assert.Equal(ItemFormat.Epub, item.Format);
        Assert.Single(item.Parts);
    }

    [Fact]
    public async Task Isbn_with_hyphens_or_ten_digits_is_stored_as_13_digits()
    {
        var section = await factory.AddSectionAsync();
        var epub = new EpubBuilder()
            .WithTitle("The Catcher in the Rye")
            .WithIdentifier("978-0-441-47812-5", scheme: "isbn")
            .WithIdentifier("0316769487", scheme: "ISBN")
            .WithIdentifier("0-441-47812-3", scheme: "isbn")
            .WithIdentifier("9780441478126", scheme: "isbn")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "catcher.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal(
            [("isbn", "9780316769488"), ("isbn", "9780441478125")],
            work.ExternalIds.Select(e => (e.Scheme, e.Value)));
    }

    [Fact]
    public async Task Identifier_with_another_scheme_is_stored_as_given_and_one_without_a_scheme_is_dropped()
    {
        var section = await factory.AddSectionAsync();
        var epub = new EpubBuilder()
            .WithTitle("Dune")
            .WithIdentifier("12345", scheme: "goodreads")
            .WithIdentifier("urn:uuid:0f3b6a6e-1c2d-4e5f-8a9b-0c1d2e3f4a5b")
            .WithIdentifier("urn:isbn:978-0-441-47812-5")
            .WithIdentifier("9d2c4e1a-3b5f-4a7c-8e9d-0a1b2c3d4e5f")
            .WithIdentifier("https://example.com/books/dune")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "dune.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal(
            [("goodreads", "12345"), ("isbn", "9780441478125"), ("uuid", "0f3b6a6e-1c2d-4e5f-8a9b-0c1d2e3f4a5b")],
            work.ExternalIds.Select(e => (e.Scheme, e.Value)));
    }

    [Fact]
    public async Task Epub_sharing_an_isbn_with_a_work_in_the_section_becomes_its_second_item()
    {
        var section = await factory.AddSectionAsync();
        var author = $"William Gibson {IngestTestExtensions.UniqueSuffix()}";
        var first = new EpubBuilder().WithTitle("Neuromancer").WithCreator(author)
            .WithIdentifier("0441569595", scheme: "isbn")
            .Build();
        var second = new EpubBuilder().WithTitle("Neuromancer (Reissue)").WithCreator(author)
            .WithIdentifier("978-0-441-56959-5", scheme: "isbn")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var firstJob = await client.IngestAsync(first, "neuromancer.epub", section.Id);
        Assert.Equal(JobStatus.Succeeded, firstJob.Status);

        var secondJob = await client.IngestAsync(second, "neuromancer-reissue.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, secondJob.Status);
        var result = secondJob.ReadIngestResult();
        Assert.Equal(IngestOutcome.Created, result.Outcome);
        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal([firstJob.ReadIngestResult().ItemId, result.ItemId], work.Items.Select(i => i.Id));
        Assert.Equal([first.Length, second.Length], work.Items.Select(i => Assert.Single(i.Parts).Size));
        Assert.Equal((1, 2, 2), await factory.CountRowsAsync(section));
    }

    [Fact]
    public async Task Second_item_in_a_folder_holding_a_file_of_the_same_name_gets_a_numbered_suffix()
    {
        var section = await factory.AddSectionAsync();
        var author = $"William Gibson {IngestTestExtensions.UniqueSuffix()}";
        EpubBuilder Edition(string description) => new EpubBuilder().WithTitle("Count Zero").WithCreator(author)
            .WithIdentifier("9780441117734", scheme: "isbn")
            .WithDescription(description);
        var first = Edition("First edition.").Build();
        var second = Edition("Second edition.").Build();
        var third = Edition("Third edition.").Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        Assert.Equal(JobStatus.Succeeded, (await client.IngestAsync(first, "count-zero.epub", section.Id)).Status);

        var secondJob = await client.IngestAsync(second, "count-zero-2.epub", section.Id);
        var thirdJob = await client.IngestAsync(third, "count-zero-3.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, secondJob.Status);
        Assert.Equal(JobStatus.Succeeded, thirdJob.Status);
        var cancellationToken = TestContext.Current.CancellationToken;
        Assert.Equal(first, await File.ReadAllBytesAsync(
            factory.LibraryPath("Books", author, "Count Zero", "Count Zero.epub"), cancellationToken));
        Assert.Equal(second, await File.ReadAllBytesAsync(
            factory.LibraryPath("Books", author, "Count Zero", "Count Zero (2).epub"), cancellationToken));
        Assert.Equal(third, await File.ReadAllBytesAsync(
            factory.LibraryPath("Books", author, "Count Zero", "Count Zero (3).epub"), cancellationToken));
        Assert.Equal(
            [
                $"Books/{author}/Count Zero/Count Zero.epub",
                $"Books/{author}/Count Zero/Count Zero (2).epub",
                $"Books/{author}/Count Zero/Count Zero (3).epub",
            ],
            await factory.ReadPartPathsAsync(section));
    }

    [Fact]
    public async Task Epub_sharing_an_isbn_with_a_work_in_another_section_makes_a_new_work()
    {
        var one = await factory.AddSectionAsync();
        var other = await factory.AddSectionAsync();
        var epub = new EpubBuilder().WithTitle("Mona Lisa Overdrive").WithCreator($"William Gibson {IngestTestExtensions.UniqueSuffix()}")
            .WithIdentifier("9780553281743", scheme: "isbn")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var first = await client.IngestAsync(epub, "mona-lisa.epub", one.Id);
        var second = await client.IngestAsync(epub, "mona-lisa.epub", other.Id);

        Assert.Equal(IngestOutcome.Created, first.ReadIngestResult().Outcome);
        Assert.Equal(IngestOutcome.Created, second.ReadIngestResult().Outcome);
        Assert.Equal((1, 1, 1), await factory.CountRowsAsync(one));
        Assert.Equal((1, 1, 1), await factory.CountRowsAsync(other));
        var inOne = await GetOnlyWorkAsync(client, one);
        var inOther = await GetOnlyWorkAsync(client, other);
        Assert.NotEqual(inOne.Id, inOther.Id);
        Assert.Equal([("isbn", "9780553281743")], inOther.ExternalIds.Select(e => (e.Scheme, e.Value)));
    }

    [Fact]
    public async Task Joining_a_work_by_isbn_leaves_its_metadata_unchanged()
    {
        var section = await factory.AddSectionAsync();
        var first = new EpubBuilder()
            .WithTitle("Pattern Recognition")
            .WithCreator($"William Gibson {IngestTestExtensions.UniqueSuffix()}")
            .WithCalibreSeries("Blue Ant", "1")
            .WithSubject("Thriller")
            .WithDescription("Cayce Pollard hunts the footage.")
            .WithPublisher("Putnam")
            .WithLanguage("en")
            .WithDate("2003-02-03")
            .WithIdentifier("9780399149863", scheme: "isbn")
            .Build();
        var second = new EpubBuilder()
            .WithTitle("Pattern Recognition: A Novel")
            .WithCreator("W. Gibson")
            .WithCalibreSeries("Bigend Trilogy", "3")
            .WithSubject("Science Fiction")
            .WithDescription("Another blurb.")
            .WithPublisher("Berkley")
            .WithLanguage("fr")
            .WithDate("2005-01-01")
            .WithIdentifier("0-399-14986-4", scheme: "isbn")
            .WithIdentifier("67890", scheme: "goodreads")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        Assert.Equal(JobStatus.Succeeded, (await client.IngestAsync(first, "pattern.epub", section.Id)).Status);
        var before = await GetOnlyWorkAsync(client, section);

        var job = await client.IngestAsync(second, "pattern-novel.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var after = await GetOnlyWorkAsync(client, section);
        Assert.Equal(2, after.Items.Count);
        Assert.Equal(WithoutItems(before), WithoutItems(after));
        Assert.Equal(
            [("isbn", "9780399149863")],
            after.ExternalIds.Select(e => (e.Scheme, e.Value)));
    }

    [Fact]
    public async Task Joining_a_work_without_a_cover_takes_the_new_cover()
    {
        var section = await factory.AddSectionAsync();
        var author = $"William Gibson {IngestTestExtensions.UniqueSuffix()}";
        var first = new EpubBuilder().WithTitle("Idoru").WithCreator(author)
            .WithIdentifier("9780425190456", scheme: "isbn")
            .Build();
        var second = new EpubBuilder().WithTitle("Idoru").WithCreator(author)
            .WithIdentifier("9780425190456", scheme: "isbn")
            .WithCover(TestData.PngCover)
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        Assert.Equal(JobStatus.Succeeded, (await client.IngestAsync(first, "idoru.epub", section.Id)).Status);
        Assert.Null((await GetOnlyWorkAsync(client, section)).CoverHash);

        var job = await client.IngestAsync(second, "idoru-with-cover.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal(2, work.Items.Count);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(TestData.PngCover)), work.CoverHash);
        var cover = factory.LibraryPath("Books", author, "Idoru", "cover.png");
        Assert.Equal(TestData.PngCover, await File.ReadAllBytesAsync(cover, TestContext.Current.CancellationToken));
        Assert.Equal($"Books/{author}/Idoru/cover.png", (await factory.ReadWorkAsync(work.Id)).CoverPath);
    }

    [Fact]
    public async Task Joining_a_work_with_a_cover_keeps_it()
    {
        var section = await factory.AddSectionAsync();
        byte[] otherCover = [0xFF, 0xD8, 0xFF, 0xE0, 5, 6, 7, 8];
        var author = $"William Gibson {IngestTestExtensions.UniqueSuffix()}";
        var first = new EpubBuilder().WithTitle("Virtual Light").WithCreator(author)
            .WithIdentifier("9780553566062", scheme: "isbn")
            .WithCover(TestData.PngCover)
            .Build();
        var second = new EpubBuilder().WithTitle("Virtual Light").WithCreator(author)
            .WithIdentifier("9780553566062", scheme: "isbn")
            .WithCover(otherCover, "cover.jpg", "image/jpeg")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        Assert.Equal(JobStatus.Succeeded, (await client.IngestAsync(first, "virtual-light.epub", section.Id)).Status);

        var job = await client.IngestAsync(second, "virtual-light-jpeg.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal(2, work.Items.Count);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(TestData.PngCover)), work.CoverHash);
        var folder = factory.LibraryPath("Books", author, "Virtual Light");
        Assert.Equal(
            TestData.PngCover,
            await File.ReadAllBytesAsync(Path.Combine(folder, "cover.png"), TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(folder, "cover.jpg")));
    }

    [Fact]
    public async Task Epub_joining_a_work_with_no_files_is_filed_under_the_works_own_title_and_first_author()
    {
        var section = await factory.AddSectionAsync();
        var translator = new Person { Name = "Some Translator", SortName = "Translator, Some" };
        var authorName = $"Stanisław Lem {IngestTestExtensions.UniqueSuffix()}";
        var author = new Person { Name = authorName, SortName = "Lem, Stanisław" };
        var work = TestData.NewWork("Solaris", section.Id);
        work.Contributors.Add(new WorkContributor { PersonId = translator.Id, Order = 0, Role = ContributorRole.Translator });
        work.Contributors.Add(new WorkContributor { PersonId = author.Id, Order = 1 });
        work.ExternalIds.Add(new WorkExternalId { Scheme = "isbn", Value = "9780156027601" });
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.AddRange(translator, author, work);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var epub = new EpubBuilder().WithTitle("Solaris: A Novel").WithCreator("S. Lem")
            .WithIdentifier("0-15-602760-7", scheme: "isbn")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "solaris.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal((1, 1, 1), await factory.CountRowsAsync(section));
        Assert.Equal([$"Books/{authorName}/Solaris/Solaris.epub"], await factory.ReadPartPathsAsync(section));
        Assert.Equal(epub, await File.ReadAllBytesAsync(
            factory.LibraryPath("Books", authorName, "Solaris", "Solaris.epub"),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Two_epubs_with_the_same_title_and_author_and_no_shared_isbn_become_two_works()
    {
        var section = await factory.AddSectionAsync();
        byte[] otherCover = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 5, 6, 7, 8];
        var author = $"William Gibson {IngestTestExtensions.UniqueSuffix()}";
        var first = new EpubBuilder().WithTitle("The Peripheral").WithCreator(author)
            .WithIdentifier("9780399158445", scheme: "isbn")
            .WithCover(TestData.PngCover)
            .Build();
        var second = new EpubBuilder().WithTitle("The Peripheral").WithCreator(author)
            .WithIdentifier("9780425276235", scheme: "isbn")
            .WithCover(otherCover)
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var firstJob = await client.IngestAsync(first, "peripheral.epub", section.Id);
        Assert.Equal(JobStatus.Succeeded, firstJob.Status);

        var secondJob = await client.IngestAsync(second, "peripheral-other.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, secondJob.Status);
        Assert.Equal(IngestOutcome.Created, secondJob.ReadIngestResult().Outcome);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var itemIds = new[] { firstJob.ReadIngestResult().ItemId, secondJob.ReadIngestResult().ItemId };
            var workIds = await db.Items.Where(i => itemIds.Contains(i.Id)).Select(i => i.WorkId)
                .Distinct().CountAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, workIds);
        }

        Assert.Equal((2, 2, 2), await factory.CountRowsAsync(section));
        Assert.Equal(
            [
                $"Books/{author}/The Peripheral/The Peripheral.epub",
                $"Books/{author}/The Peripheral (2)/The Peripheral.epub",
            ],
            await factory.ReadPartPathsAsync(section));
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstFolder = factory.LibraryPath("Books", author, "The Peripheral");
        var secondFolder = factory.LibraryPath("Books", author, "The Peripheral (2)");
        Assert.Equal(first, await File.ReadAllBytesAsync(Path.Combine(firstFolder, "The Peripheral.epub"), cancellationToken));
        Assert.Equal(TestData.PngCover, await File.ReadAllBytesAsync(Path.Combine(firstFolder, "cover.png"), cancellationToken));
        Assert.Equal(second, await File.ReadAllBytesAsync(Path.Combine(secondFolder, "The Peripheral.epub"), cancellationToken));
        Assert.Equal(otherCover, await File.ReadAllBytesAsync(Path.Combine(secondFolder, "cover.png"), cancellationToken));
        Assert.Equal(["The Peripheral.epub", "cover.png"], Directory.GetFiles(firstFolder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Two_epubs_sharing_a_placeholder_isbn_become_two_works()
    {
        var section = await factory.AddSectionAsync();
        var author = $"William Gibson {IngestTestExtensions.UniqueSuffix()}";
        var first = new EpubBuilder().WithTitle("Burning Chrome").WithCreator(author)
            .WithIdentifier("0000000000", scheme: "isbn")
            .Build();
        var second = new EpubBuilder().WithTitle("Zero History").WithCreator(author)
            .WithIdentifier("0000000000", scheme: "isbn")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        Assert.Equal(JobStatus.Succeeded, (await client.IngestAsync(first, "burning-chrome.epub", section.Id)).Status);
        Assert.Equal(JobStatus.Succeeded, (await client.IngestAsync(second, "zero-history.epub", section.Id)).Status);

        var works = await GetWorksAsync(client, section);
        Assert.Equal(["Burning Chrome", "Zero History"], works.Select(w => w.Title).Order(StringComparer.Ordinal));
        Assert.All(works, w => Assert.DoesNotContain(w.ExternalIds, e => e.Scheme == "isbn"));
        Assert.Equal((2, 2, 2), await factory.CountRowsAsync(section));
    }

    [Fact]
    public async Task Title_and_author_with_illegal_characters_are_filed_under_safe_names()
    {
        var section = await factory.AddSectionAsync();
        const string title = "What? A \"Test\": Part 1/2...";
        var author = string.Concat(Enumerable.Repeat("Longname ", 16)) + "Smith.";
        Assert.Equal(150, author.Length);
        var epub = new EpubBuilder().WithTitle(title).WithCreator(author).Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "illegal.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal(title, work.Title);
        Assert.Equal(author, Assert.Single(work.Contributors).Name);
        var path = Assert.Single(await factory.ReadPartPathsAsync(section));
        var segments = path.Split('/');
        Assert.Equal(4, segments.Length);
        Assert.Equal("Books", segments[0]);
        Assert.All(segments, segment =>
        {
            Assert.DoesNotContain(segment, c => "<>:\"/\\|?*".Contains(c) || char.IsControl(c));
            Assert.False(segment.EndsWith('.') || segment.EndsWith(' '), $"'{segment}' ends with a dot or a space.");
            Assert.InRange(segment.Length, 1, 105);
        });
        Assert.Equal("What_ A _Test__ Part 1_2", segments[2]);
        Assert.Equal("What_ A _Test__ Part 1_2.epub", segments[3]);
        var filed = factory.LibraryPath(segments);
        Assert.Equal(epub, await File.ReadAllBytesAsync(filed, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Epub_with_no_title_and_no_author_is_titled_after_its_file_name()
    {
        var section = await factory.AddSectionAsync();
        var epub = new EpubBuilder().Build();
        var title = $"mystery-book {IngestTestExtensions.UniqueSuffix()}";
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, $"{title}.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal(title, work.Title);
        Assert.Empty(work.Contributors);
        Assert.Equal([$"Books/Unknown Author/{title}/{title}.epub"], await factory.ReadPartPathsAsync(section));
        Assert.True(File.Exists(factory.LibraryPath("Books", "Unknown Author", title, $"{title}.epub")));
    }

    [Theory]
    [InlineData(@"C:\Users\reader\Downloads\")]
    [InlineData("../../")]
    public async Task Title_taken_from_the_file_name_leaves_out_any_folder_in_it(string folder)
    {
        var section = await factory.AddSectionAsync();
        var epub = new EpubBuilder().Build();
        var title = $"stray-book {IngestTestExtensions.UniqueSuffix()}";
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, $"{folder}{title}.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(title, (await GetOnlyWorkAsync(client, section)).Title);
        Assert.Equal([$"Books/Unknown Author/{title}/{title}.epub"], await factory.ReadPartPathsAsync(section));
    }

    [Fact]
    public async Task Sort_title_and_sort_name_come_from_the_file_when_it_has_them()
    {
        var section = await factory.AddSectionAsync();
        var author = $"Ursula K. Le Guin {IngestTestExtensions.UniqueSuffix()}";
        var epub = new EpubBuilder(version: 2)
            .WithTitle("The Left Hand of Darkness")
            .WithMeta("calibre:title_sort", "Left Hand of Darkness, The")
            .WithCreator(author, fileAs: "Le Guin, Ursula K.")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "left-hand.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal("The Left Hand of Darkness", work.Title);
        Assert.Equal("Left Hand of Darkness, The", work.SortTitle);
        Assert.Equal(
            [(author, "Le Guin, Ursula K.")],
            await ReadPeopleAsync(work.Contributors.Select(c => c.PersonId)));
    }

    [Fact]
    public async Task Sort_title_and_sort_name_fall_back_to_the_display_values()
    {
        var section = await factory.AddSectionAsync();
        var author = $"Ursula K. Le Guin {IngestTestExtensions.UniqueSuffix()}";
        var epub = new EpubBuilder(version: 3)
            .WithTitle("The Dispossessed")
            .WithCreator(author, role: "aut")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "dispossessed.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal("The Dispossessed", work.SortTitle);
        Assert.Equal(
            [(author, author)],
            await ReadPeopleAsync(work.Contributors.Select(c => c.PersonId)));
    }

    [Fact]
    public async Task Creators_take_their_roles_and_unknown_roles_are_skipped()
    {
        var section = await factory.AddSectionAsync();
        var author = $"Stanisław Lem {IngestTestExtensions.UniqueSuffix()}";
        var epub = new EpubBuilder(version: 3)
            .WithTitle("Solaris")
            .WithCreator("Bill Johnston", role: "trl")
            .WithCreator("Some Illustrator", role: "ill")
            .WithCreator(author, role: "aut")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "solaris.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal(
            [("Bill Johnston", ContributorRole.Translator), (author, ContributorRole.Author)],
            work.Contributors.Select(c => (c.Name, c.Role)));
        Assert.Equal([0, 1], await ReadContributorOrdersAsync(work.Id));
        Assert.Equal([$"Books/{author}/Solaris/Solaris.epub"], await factory.ReadPartPathsAsync(section));
    }

    [Theory]
    [InlineData(null, ContributorRole.Author)]
    [InlineData(" AUT ", ContributorRole.Author)]
    [InlineData("Trl", ContributorRole.Translator)]
    [InlineData("edt", ContributorRole.Editor)]
    [InlineData(" nrt ", ContributorRole.Narrator)]
    public async Task Creator_role_codes_are_read_ignoring_case_and_surrounding_spaces(
        string? code, ContributorRole role)
    {
        var section = await factory.AddSectionAsync();
        var name = $"Pat Contributor {IngestTestExtensions.UniqueSuffix()}";
        var epub = new EpubBuilder(version: 2).WithTitle("Roles").WithCreator(name, role: code).Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "roles.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal([(name, role)], work.Contributors.Select(c => (c.Name, c.Role)));
    }

    [Fact]
    public async Task Book_with_no_author_among_its_creators_is_filed_under_unknown_author()
    {
        var section = await factory.AddSectionAsync();
        var title = $"Anthology {IngestTestExtensions.UniqueSuffix()}";
        var epub = new EpubBuilder(version: 3)
            .WithTitle(title)
            .WithCreator("Ed Itor", role: "edt")
            .WithCreator("Some Illustrator", role: "ill")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "anthology.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal([("Ed Itor", ContributorRole.Editor)], work.Contributors.Select(c => (c.Name, c.Role)));
        Assert.Equal([$"Books/Unknown Author/{title}/{title}.epub"], await factory.ReadPartPathsAsync(section));
    }

    [Fact]
    public async Task Authors_differing_only_in_case_or_spacing_share_one_person()
    {
        var section = await factory.AddSectionAsync();
        var suffix = IngestTestExtensions.UniqueSuffix();
        var first = new EpubBuilder().WithTitle("The Word for World Is Forest")
            .WithCreator($"Ursula K. Le Guin {suffix}", fileAs: "Le Guin, Ursula K.")
            .Build();
        var second = new EpubBuilder().WithTitle("The Telling")
            .WithCreator($"ursula  k. le guin {suffix} ", fileAs: "le guin, ursula k.")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        Assert.Equal(JobStatus.Succeeded, (await client.IngestAsync(first, "word-for-world.epub", section.Id)).Status);
        Assert.Equal(JobStatus.Succeeded, (await client.IngestAsync(second, "telling.epub", section.Id)).Status);

        var works = await GetWorksAsync(client, section);
        var personIds = works.Select(w => Assert.Single(w.Contributors).PersonId).Distinct().ToList();
        var personId = Assert.Single(personIds);
        Assert.Equal(
            [($"Ursula K. Le Guin {suffix}", "Le Guin, Ursula K.")],
            await ReadPeopleAsync([personId]));
        Assert.Equal(1, await factory.CountNamedAsync<Person>(suffix));
    }

    [Fact]
    public async Task Book_by_a_person_already_here_is_filed_under_that_persons_stored_name()
    {
        var section = await factory.AddSectionAsync();
        var suffix = IngestTestExtensions.UniqueSuffix();
        var first = new EpubBuilder().WithTitle("Rocannon's World").WithCreator($"Ursula K. Le Guin {suffix}").Build();
        var second = new EpubBuilder().WithTitle("Planet of Exile").WithCreator($"ursula  k. le guin {suffix} ").Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        Assert.Equal(JobStatus.Succeeded, (await client.IngestAsync(first, "rocannon.epub", section.Id)).Status);
        Assert.Equal(JobStatus.Succeeded, (await client.IngestAsync(second, "exile.epub", section.Id)).Status);

        Assert.Equal(
            [
                $"Books/Ursula K. Le Guin {suffix}/Rocannon's World/Rocannon's World.epub",
                $"Books/Ursula K. Le Guin {suffix}/Planet of Exile/Planet of Exile.epub",
            ],
            await factory.ReadPartPathsAsync(section));
    }

    [Fact]
    public async Task Series_differing_only_in_case_or_spacing_share_one_row()
    {
        var section = await factory.AddSectionAsync();
        var suffix = IngestTestExtensions.UniqueSuffix();
        var first = new EpubBuilder().WithTitle("Rocannon's World").WithCalibreSeries($"Hainish Cycle {suffix}", "1").Build();
        var second = new EpubBuilder(version: 3).WithTitle("Planet of Exile")
            .WithCollection($" HAINISH   cycle {suffix}", "2")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        Assert.Equal(JobStatus.Succeeded, (await client.IngestAsync(first, "rocannon.epub", section.Id)).Status);
        Assert.Equal(JobStatus.Succeeded, (await client.IngestAsync(second, "exile.epub", section.Id)).Status);

        var works = await GetWorksAsync(client, section);
        Assert.Equal(
            [("Planet of Exile", 2m), ("Rocannon's World", 1m)],
            works.Select(w => (w.Title, w.Series!.Position)).Order());
        var series = Assert.Single(works.Select(w => (w.Series!.Id, w.Series.Name)).Distinct());
        Assert.Equal($"Hainish Cycle {suffix}", series.Name);
        Assert.Equal(1, await factory.CountNamedAsync<Series>(suffix));
    }

    [Fact]
    public async Task Tags_differing_only_in_case_or_spacing_share_one_row()
    {
        var section = await factory.AddSectionAsync();
        var suffix = IngestTestExtensions.UniqueSuffix();
        var first = new EpubBuilder().WithTitle("The Lathe of Heaven").WithSubject($"Science Fiction {suffix}").Build();
        var second = new EpubBuilder().WithTitle("City of Illusions").WithSubject($"science\tFICTION  {suffix}").Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        Assert.Equal(JobStatus.Succeeded, (await client.IngestAsync(first, "lathe.epub", section.Id)).Status);
        Assert.Equal(JobStatus.Succeeded, (await client.IngestAsync(second, "illusions.epub", section.Id)).Status);

        var works = await GetWorksAsync(client, section);
        Assert.Equal(2, works.Count);
        var tag = Assert.Single(works.Select(w => Assert.Single(w.Tags)).Distinct());
        Assert.Equal($"Science Fiction {suffix}", tag.Name);
        Assert.Equal(1, await factory.CountNamedAsync<Tag>(suffix));
    }

    [Fact]
    public async Task Author_stored_with_irregular_spacing_is_reused()
    {
        var section = await factory.AddSectionAsync();
        var suffix = IngestTestExtensions.UniqueSuffix();
        var stored = new Person { Name = $"Ursula  K.  Le Guin {suffix}", SortName = "Le Guin, Ursula K." };
        await SaveAsync(stored);
        var epub = new EpubBuilder().WithTitle("The Telling").WithCreator($"ursula k. le guin {suffix}").Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "telling.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal(stored.Id, Assert.Single(work.Contributors).PersonId);
        Assert.Equal(1, await factory.CountNamedAsync<Person>(suffix));
    }

    [Fact]
    public async Task Author_with_letters_the_database_lowercases_differently_is_reused()
    {
        var section = await factory.AddSectionAsync();
        var suffix = IngestTestExtensions.UniqueSuffix();
        var name = $"İhsan Oktay Anar {suffix}";
        var first = new EpubBuilder().WithTitle("Puslu Kıtalar Atlası").WithCreator(name).Build();
        var second = new EpubBuilder().WithTitle("Kitab-ül Hiyel").WithCreator(NameKey.Of(name)).Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        Assert.Equal(JobStatus.Succeeded, (await client.IngestAsync(first, "atlas.epub", section.Id)).Status);
        Assert.Equal(JobStatus.Succeeded, (await client.IngestAsync(second, "hiyel.epub", section.Id)).Status);

        var works = await GetWorksAsync(client, section);
        Assert.Single(works.Select(w => Assert.Single(w.Contributors).PersonId).Distinct());
        Assert.Equal(1, await factory.CountNamedAsync<Person>(suffix));
    }

    [Fact]
    public async Task Series_stored_with_irregular_spacing_is_reused()
    {
        var section = await factory.AddSectionAsync();
        var suffix = IngestTestExtensions.UniqueSuffix();
        var stored = new Series { Name = $"Hainish  Cycle  {suffix}" };
        await SaveAsync(stored);
        var epub = new EpubBuilder().WithTitle("The Telling").WithCalibreSeries($"hainish cycle {suffix}", "8").Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "telling.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal(stored.Id, work.Series!.Id);
        Assert.Equal(1, await factory.CountNamedAsync<Series>(suffix));
    }

    [Fact]
    public async Task Cover_named_like_a_book_does_not_replace_the_book()
    {
        var section = await factory.AddSectionAsync();
        var author = $"Cover Author {IngestTestExtensions.UniqueSuffix()}";
        var epub = new EpubBuilder().WithTitle("cover").WithCreator(author)
            .WithCover(TestData.PngCover, "x.epub", "image/png")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "cover.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal([$"Books/{author}/cover/cover.epub"], await factory.ReadPartPathsAsync(section));
        var cancellationToken = TestContext.Current.CancellationToken;
        Assert.Equal(epub, await File.ReadAllBytesAsync(
            factory.LibraryPath("Books", author, "cover", "cover.epub"), cancellationToken));
        Assert.Equal(TestData.PngCover, await File.ReadAllBytesAsync(
            factory.LibraryPath("Books", author, "cover", "cover.png"), cancellationToken));
    }

    [Fact]
    public async Task Tag_stored_with_irregular_spacing_is_reused()
    {
        var section = await factory.AddSectionAsync();
        var suffix = IngestTestExtensions.UniqueSuffix();
        var stored = new Tag { Name = $"Science  Fiction  {suffix}" };
        await SaveAsync(stored);
        var epub = new EpubBuilder().WithTitle("The Telling").WithSubject($"science fiction {suffix}").Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "telling.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal(stored.Id, Assert.Single(work.Tags).Id);
        Assert.Equal(1, await factory.CountNamedAsync<Tag>(suffix));
    }

    [Fact]
    public async Task Subject_repeated_in_one_file_is_one_tag()
    {
        var section = await factory.AddSectionAsync();
        var suffix = IngestTestExtensions.UniqueSuffix();
        var epub = new EpubBuilder().WithTitle("The Eye of the Heron")
            .WithSubject($"  Space   Opera {suffix}")
            .WithSubject($"space opera {suffix}")
            .WithSubject($"Utopia {suffix}")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "heron.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal([$"Space Opera {suffix}", $"Utopia {suffix}"], work.Tags.Select(t => t.Name));
        Assert.Equal(2, await factory.CountNamedAsync<Tag>(suffix));
    }

    [Fact]
    public async Task Creator_repeated_in_one_file_is_one_person_who_keeps_each_role_once()
    {
        var section = await factory.AddSectionAsync();
        var suffix = IngestTestExtensions.UniqueSuffix();
        var epub = new EpubBuilder(version: 3).WithTitle("Self-Translated")
            .WithCreator($"Pat Writer {suffix}", role: "aut")
            .WithCreator($"PAT  writer {suffix}", role: "aut")
            .WithCreator($"pat writer {suffix}", role: "trl")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "self-translated.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal(
            [($"Pat Writer {suffix}", ContributorRole.Author), ($"Pat Writer {suffix}", ContributorRole.Translator)],
            work.Contributors.Select(c => (c.Name, c.Role)));
        Assert.Single(work.Contributors.Select(c => c.PersonId).Distinct());
        Assert.Equal(1, await factory.CountNamedAsync<Person>(suffix));
        Assert.Equal([0, 1], await ReadContributorOrdersAsync(work.Id));
    }

    [Fact]
    public async Task Uploaded_epub_is_filed_under_its_author_and_title_and_leaves_staging()
    {
        var section = await factory.AddSectionAsync();
        var author = $"Ursula K. Le Guin {IngestTestExtensions.UniqueSuffix()}";
        var epub = new EpubBuilder().WithTitle("The Left Hand of Darkness").WithCreator(author).Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var stagedBefore = factory.CountStagedFiles();

        var job = await client.IngestAsync(epub, "upload.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var path = factory.LibraryPath("Books", author, "The Left Hand of Darkness", "The Left Hand of Darkness.epub");
        Assert.True(File.Exists(path), $"Expected the book at {path}.");
        Assert.Equal(epub, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        await factory.WaitForStagedFilesAsync(stagedBefore);
    }

    [Fact]
    public async Task Uploaded_epub_is_filed_under_the_one_library_folder()
    {
        var section = await factory.AddSectionAsync();
        var author = $"Ursula K. Le Guin {IngestTestExtensions.UniqueSuffix()}";
        var epub = new EpubBuilder().WithTitle("The Lathe of Heaven").WithCreator(author).Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "lathe.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var path = Path.Combine(
            factory.DataPath, "library", "Books", author, "The Lathe of Heaven", "The Lathe of Heaven.epub");
        Assert.True(File.Exists(path), $"Expected the book at {path}.");
        Assert.Equal(epub, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(["Books"], Directory.GetFileSystemEntries(factory.LibraryPath()).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Same_author_and_title_in_two_sections_are_filed_in_separate_folders()
    {
        var one = await factory.AddSectionAsync();
        var other = await factory.AddSectionAsync();
        var author = $"Ursula K. Le Guin {IngestTestExtensions.UniqueSuffix()}";
        var first = new EpubBuilder().WithTitle("The Telling").WithCreator(author).WithDescription("First.").Build();
        var second = new EpubBuilder().WithTitle("The Telling").WithCreator(author).WithDescription("Second.").Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var firstJob = await client.IngestAsync(first, "telling.epub", one.Id);
        var secondJob = await client.IngestAsync(second, "telling.epub", other.Id);

        Assert.Equal(JobStatus.Succeeded, firstJob.Status);
        Assert.Equal(JobStatus.Succeeded, secondJob.Status);
        Assert.Equal([$"Books/{author}/The Telling/The Telling.epub"], await factory.ReadPartPathsAsync(one));
        Assert.Equal([$"Books/{author}/The Telling (2)/The Telling.epub"], await factory.ReadPartPathsAsync(other));
        var cancellationToken = TestContext.Current.CancellationToken;
        var books = factory.LibraryPath("Books", author);
        Assert.Equal(first, await File.ReadAllBytesAsync(
            Path.Combine(books, "The Telling", "The Telling.epub"), cancellationToken));
        Assert.Equal(second, await File.ReadAllBytesAsync(
            Path.Combine(books, "The Telling (2)", "The Telling.epub"), cancellationToken));
    }

    [Fact]
    public async Task Book_is_filed_over_a_partial_file_left_by_a_dead_run()
    {
        var section = await factory.AddSectionAsync();
        var author = $"Octavia E. Butler {IngestTestExtensions.UniqueSuffix()}";
        var epub = new EpubBuilder().WithTitle("Dawn").WithCreator(author).WithCover(TestData.PngCover).Build();
        var folder = factory.LibraryPath("Books", author, "Dawn");
        var cancellationToken = TestContext.Current.CancellationToken;

        // What a run that died while copying leaves: part of the book and a cover, with no rows pointing at them.
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "Dawn.epub"), epub[..(epub.Length / 2)], cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(folder, "cover.png"), [1, 2, 3], cancellationToken);
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "dawn.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal([$"Books/{author}/Dawn/Dawn.epub"], await factory.ReadPartPathsAsync(section));
        Assert.Equal(epub, await File.ReadAllBytesAsync(Path.Combine(folder, "Dawn.epub"), cancellationToken));
        Assert.Equal(TestData.PngCover, await File.ReadAllBytesAsync(Path.Combine(folder, "cover.png"), cancellationToken));
        Assert.Equal(["Dawn.epub", "cover.png"], Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(TestData.PngCover)), work.CoverHash);
    }

    [Fact]
    public async Task Png_cover_is_written_beside_the_book_and_hashed_on_the_work()
    {
        var section = await factory.AddSectionAsync();
        var author = $"Octavia E. Butler {IngestTestExtensions.UniqueSuffix()}";
        var epub = new EpubBuilder().WithTitle("Kindred").WithCreator(author).WithCover(TestData.PngCover).Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "kindred.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var cover = factory.LibraryPath("Books", author, "Kindred", "cover.png");
        Assert.True(File.Exists(cover), $"Expected the cover at {cover}.");
        Assert.Equal(TestData.PngCover, await File.ReadAllBytesAsync(cover, TestContext.Current.CancellationToken));
        var work = await GetOnlyWorkAsync(client, section);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(TestData.PngCover)), work.CoverHash);
    }

    [Fact]
    public async Task Epub_with_a_cover_records_the_cover_files_path()
    {
        var section = await factory.AddSectionAsync();
        var author = $"Octavia E. Butler {IngestTestExtensions.UniqueSuffix()}";
        var epub = new EpubBuilder().WithTitle("Bloodchild").WithCreator(author)
            .WithCover(TestData.PngCover)
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "bloodchild.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var work = await factory.ReadWorkAsync((await GetOnlyWorkAsync(client, section)).Id);
        Assert.Equal($"Books/{author}/Bloodchild/cover.png", work.CoverPath);
        Assert.Equal(
            TestData.PngCover,
            await File.ReadAllBytesAsync(
                factory.LibraryPath(work.CoverPath!.Split('/')), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Epub_without_a_cover_records_no_cover_path()
    {
        var section = await factory.AddSectionAsync();
        var epub = new EpubBuilder().WithTitle("Fledgling").WithCreator($"Octavia E. Butler {IngestTestExtensions.UniqueSuffix()}").Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "fledgling.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var work = await factory.ReadWorkAsync((await GetOnlyWorkAsync(client, section)).Id);
        Assert.Null(work.CoverPath);
    }

    [Fact]
    public async Task Epub_with_an_oversized_cover_is_ingested_without_one()
    {
        var section = await factory.AddSectionAsync();
        var author = $"Octavia E. Butler {IngestTestExtensions.UniqueSuffix()}";
        // Zero bytes compress to almost nothing, so the upload stays small while the cover inflates past 20 MiB.
        var epub = new EpubBuilder()
            .WithTitle("Wild Seed")
            .WithCreator(author)
            .WithCover(new byte[21 * 1024 * 1024])
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "wild-seed.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Null((await GetOnlyWorkAsync(client, section)).CoverHash);
        var folder = factory.LibraryPath("Books", author, "Wild Seed");
        Assert.Equal(["Wild Seed.epub"], Directory.GetFiles(folder).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Work_created_by_ingest_has_an_empty_search_vector()
    {
        var section = await factory.AddSectionAsync();
        var epub = new EpubBuilder().WithTitle("Parable of the Sower").WithCreator($"Octavia E. Butler {IngestTestExtensions.UniqueSuffix()}").Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "sower.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var work = await db.Works.SingleAsync(w => w.SectionId == section.Id, TestContext.Current.CancellationToken);
        Assert.Null(work.SearchVector);
    }

    [Fact]
    public async Task Same_file_uploaded_twice_to_one_section_is_a_duplicate_of_the_first_item()
    {
        var section = await factory.AddSectionAsync();
        var epub = new EpubBuilder().WithTitle("The Dispossessed").WithCreator($"Ursula K. Le Guin {IngestTestExtensions.UniqueSuffix()}").Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        // Read before the first upload: a succeeded job's staged file goes a moment after the job reads as succeeded,
        // so a count read between the two uploads could still hold the first one's.
        var stagedBefore = factory.CountStagedFiles();
        var first = await client.IngestAsync(epub, "dispossessed.epub", section.Id);
        Assert.Equal(JobStatus.Succeeded, first.Status);

        var second = await client.IngestAsync(epub, "dispossessed-again.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, second.Status);
        var result = second.ReadIngestResult();
        Assert.Equal(IngestOutcome.Duplicate, result.Outcome);
        Assert.Equal(first.ReadIngestResult().ItemId, result.ItemId);
        Assert.Equal(["identify-format", "check-duplicate"], second.Steps.Select(s => s.Name));
        Assert.Equal((1, 1, 1), await factory.CountRowsAsync(section));
        await factory.WaitForStagedFilesAsync(stagedBefore);
    }

    [Fact]
    public async Task Ingest_result_names_the_work_a_new_file_went_into()
    {
        var section = await factory.AddSectionAsync();
        var epub = new EpubBuilder().WithTitle("The Word for World Is Forest").WithCreator($"Ursula K. Le Guin {IngestTestExtensions.UniqueSuffix()}").Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "word-for-world.epub", section.Id);

        var result = job.ReadIngestResult();
        Assert.Equal(IngestOutcome.Created, result.Outcome);
        Assert.Equal((await GetOnlyWorkAsync(client, section)).Id, result.WorkId);
    }

    [Fact]
    public async Task Ingest_result_of_a_duplicate_names_the_work_that_already_holds_the_file()
    {
        var section = await factory.AddSectionAsync();
        var epub = new EpubBuilder().WithTitle("Rocannon's World").WithCreator($"Ursula K. Le Guin {IngestTestExtensions.UniqueSuffix()}").Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var first = await client.IngestAsync(epub, "rocannon.epub", section.Id);
        Assert.Equal(JobStatus.Succeeded, first.Status);

        var second = await client.IngestAsync(epub, "rocannon-again.epub", section.Id);

        var result = second.ReadIngestResult();
        Assert.Equal(IngestOutcome.Duplicate, result.Outcome);
        Assert.Equal((await GetOnlyWorkAsync(client, section)).Id, result.WorkId);
    }

    [Fact]
    public async Task Same_file_uploaded_to_two_sections_makes_a_work_in_each()
    {
        var one = await factory.AddSectionAsync();
        var other = await factory.AddSectionAsync();
        var epub = new EpubBuilder().WithTitle("The Lathe of Heaven").WithCreator($"Ursula K. Le Guin {IngestTestExtensions.UniqueSuffix()}").Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var first = await client.IngestAsync(epub, "lathe.epub", one.Id);
        var second = await client.IngestAsync(epub, "lathe.epub", other.Id);

        Assert.Equal(IngestOutcome.Created, first.ReadIngestResult().Outcome);
        Assert.Equal(IngestOutcome.Created, second.ReadIngestResult().Outcome);
        Assert.NotEqual(first.ReadIngestResult().ItemId, second.ReadIngestResult().ItemId);
        Assert.Equal((1, 1, 1), await factory.CountRowsAsync(one));
        Assert.Equal((1, 1, 1), await factory.CountRowsAsync(other));
    }

    [Fact]
    public async Task Epub_uploaded_without_a_section_goes_to_the_default_area()
    {
        var suffix = IngestTestExtensions.UniqueSuffix();
        var epub = new EpubBuilder().WithTitle($"Always Coming Home {suffix}").WithCreator($"Ursula K. Le Guin {suffix}")
            .WithIdentifier("9780765326355", scheme: "isbn")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var job = await client.IngestAsync(epub, "always-coming-home.epub", sectionId: null);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var result = job.ReadIngestResult();
        Assert.Equal(IngestOutcome.Created, result.Outcome);
        var work = await client.GetAsync<WorkDetailResponse>($"/api/works/{result.WorkId}");
        Assert.Null(work.SectionId);
        Assert.Equal([result.ItemId], work.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Same_file_uploaded_twice_to_the_default_area_is_a_duplicate()
    {
        var suffix = IngestTestExtensions.UniqueSuffix();
        var epub = new EpubBuilder().WithTitle($"Four Ways to Forgiveness {suffix}").WithCreator($"Ursula K. Le Guin {suffix}")
            .WithIdentifier("9780547928227", scheme: "isbn")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var first = await client.IngestAsync(epub, "four-ways.epub", sectionId: null);
        Assert.Equal(JobStatus.Succeeded, first.Status);

        var second = await client.IngestAsync(epub, "four-ways-again.epub", sectionId: null);

        Assert.Equal(JobStatus.Succeeded, second.Status);
        var result = second.ReadIngestResult();
        Assert.Equal(IngestOutcome.Duplicate, result.Outcome);
        Assert.Equal(first.ReadIngestResult().WorkId, result.WorkId);
        Assert.Equal(first.ReadIngestResult().ItemId, result.ItemId);
    }

    [Fact]
    public async Task Same_file_uploaded_to_a_section_and_to_the_default_area_makes_a_work_in_each()
    {
        var section = await factory.AddSectionAsync();
        var suffix = IngestTestExtensions.UniqueSuffix();
        var epub = new EpubBuilder().WithTitle($"The Telling {suffix}").WithCreator($"Ursula K. Le Guin {suffix}")
            .WithIdentifier("9780061120084", scheme: "isbn")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var inSection = await client.IngestAsync(epub, "telling.epub", section.Id);
        var inDefault = await client.IngestAsync(epub, "telling.epub", sectionId: null);

        Assert.Equal(IngestOutcome.Created, inSection.ReadIngestResult().Outcome);
        Assert.Equal(IngestOutcome.Created, inDefault.ReadIngestResult().Outcome);
        Assert.NotEqual(inSection.ReadIngestResult().WorkId, inDefault.ReadIngestResult().WorkId);
        Assert.Equal((1, 1, 1), await factory.CountRowsAsync(section));
        var defaultWork = await client.GetAsync<WorkDetailResponse>($"/api/works/{inDefault.ReadIngestResult().WorkId}");
        Assert.Null(defaultWork.SectionId);
    }

    [Fact]
    public async Task Epub_sharing_an_isbn_with_a_work_in_the_default_area_joins_it()
    {
        var suffix = IngestTestExtensions.UniqueSuffix();
        var author = $"Ursula K. Le Guin {suffix}";
        var first = new EpubBuilder().WithTitle($"Tehanu {suffix}").WithCreator(author)
            .WithIdentifier("9780143127741", scheme: "isbn")
            .Build();
        var second = new EpubBuilder().WithTitle($"Tehanu (Reissue) {suffix}").WithCreator(author)
            .WithIdentifier("9780143127741", scheme: "isbn")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var firstJob = await client.IngestAsync(first, "tehanu.epub", sectionId: null);
        Assert.Equal(JobStatus.Succeeded, firstJob.Status);

        var secondJob = await client.IngestAsync(second, "tehanu-reissue.epub", sectionId: null);

        Assert.Equal(JobStatus.Succeeded, secondJob.Status);
        var result = secondJob.ReadIngestResult();
        Assert.Equal(IngestOutcome.Created, result.Outcome);
        Assert.Equal(firstJob.ReadIngestResult().WorkId, result.WorkId);
        var work = await client.GetAsync<WorkDetailResponse>($"/api/works/{result.WorkId}");
        Assert.Null(work.SectionId);
        Assert.Equal([firstJob.ReadIngestResult().ItemId, result.ItemId], work.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Epub_sharing_an_isbn_with_a_work_in_the_default_area_makes_a_new_work_in_a_section()
    {
        var section = await factory.AddSectionAsync();
        var suffix = IngestTestExtensions.UniqueSuffix();
        var author = $"Ursula K. Le Guin {suffix}";
        var first = new EpubBuilder().WithTitle($"The Other Wind {suffix}").WithCreator(author)
            .WithIdentifier("9780307588364", scheme: "isbn")
            .Build();
        var second = new EpubBuilder().WithTitle($"The Other Wind (Reissue) {suffix}").WithCreator(author)
            .WithIdentifier("9780307588364", scheme: "isbn")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var firstJob = await client.IngestAsync(first, "other-wind.epub", sectionId: null);
        Assert.Equal(JobStatus.Succeeded, firstJob.Status);

        var secondJob = await client.IngestAsync(second, "other-wind-reissue.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, secondJob.Status);
        var result = secondJob.ReadIngestResult();
        Assert.Equal(IngestOutcome.Created, result.Outcome);
        Assert.NotEqual(firstJob.ReadIngestResult().WorkId, result.WorkId);
        Assert.Equal((1, 1, 1), await factory.CountRowsAsync(section));
        var defaultWork = await client.GetAsync<WorkDetailResponse>($"/api/works/{firstJob.ReadIngestResult().WorkId}");
        Assert.Null(defaultWork.SectionId);
        Assert.Single(defaultWork.Items);
    }

    [Fact]
    public async Task Drm_encrypted_epub_fails_its_job_and_creates_nothing()
    {
        var epub = new EpubBuilder().WithTitle("Locked").WithCreator("Some Author")
            .WithEncryption(EpubBuilder.Aes128Cbc)
            .Build();

        await AssertFailsAtMetadataAndCreatesNothingAsync(epub, "DRM-encrypted");
    }

    [Fact]
    public async Task Unreadable_epub_fails_its_job_and_creates_nothing()
    {
        var epub = new EpubBuilder().WithTitle("Broken").WithCreator("Some Author")
            .WithFile("OEBPS/content.opf", "<package><metadata>")
            .Build();

        await AssertFailsAtMetadataAndCreatesNothingAsync(epub, "cannot be read");
    }

    [Fact]
    public async Task Failed_ingest_keeps_its_staged_file_and_runs_again_when_retried()
    {
        var section = await factory.AddSectionAsync();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var stagedBefore = factory.CountStagedFiles();

        var failed = await client.IngestAsync(TestData.NotABook, "notes.epub", section.Id);

        Assert.Equal(JobStatus.Failed, failed.Status);
        Assert.Equal(stagedBefore + 1, factory.CountStagedFiles());
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var waiting = await client.GetAsync<JobDetailResponse>($"/api/jobs/{failed.Id}");
        Assert.Equal(JobStatus.Failed, waiting.Status);
        Assert.Equal(1, waiting.AttemptsMade);

        var retry = await client.RetryJobAsync(failed.Id);

        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        var retried = await client.WaitForJobAsync(failed.Id);
        Assert.Equal(JobStatus.Failed, retried.Status);
        Assert.Contains("unsupported format", retried.Error);
        Assert.Equal(
            [("identify-format", 1), ("identify-format", 2)],
            retried.Steps.Select(s => (s.Name, s.Attempt)));
        Assert.Equal(stagedBefore + 1, factory.CountStagedFiles());
    }

    [Fact]
    public async Task Deleting_a_failed_ingest_removes_the_job_its_steps_and_its_staged_file()
    {
        var section = await factory.AddSectionAsync();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var stagedBefore = factory.CountStagedFiles();
        var failed = await client.IngestAsync(TestData.NotABook, "notes.epub", section.Id);
        Assert.Equal(JobStatus.Failed, failed.Status);
        Assert.Equal(stagedBefore + 1, factory.CountStagedFiles());

        var response = await client.DeleteJobAsync(failed.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var gone = await client.GetAsync($"/api/jobs/{failed.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        Assert.False(await db.JobSteps.AnyAsync(s => s.JobId == failed.Id, TestContext.Current.CancellationToken));
        Assert.Equal(stagedBefore, factory.CountStagedFiles());
    }

    [Fact]
    public async Task Deleting_a_succeeded_ingest_leaves_the_filed_book_in_place()
    {
        var section = await factory.AddSectionAsync();
        var author = $"Octavia E. Butler {IngestTestExtensions.UniqueSuffix()}";
        var epub = new EpubBuilder().WithTitle("Fledgling").WithCreator(author).Build();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var job = await client.IngestAsync(epub, "fledgling.epub", section.Id);
        Assert.Equal(JobStatus.Succeeded, job.Status);

        var response = await client.DeleteJobAsync(job.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var path = factory.LibraryPath("Books", author, "Fledgling", "Fledgling.epub");
        Assert.Equal(epub, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal((1, 1, 1), await factory.CountRowsAsync(section));
    }

    /// <summary>Ingests the file into a section of its own and checks the job failed reading it, leaving the section empty in the database and the library folder as it was on disk.</summary>
    private async Task AssertFailsAtMetadataAndCreatesNothingAsync(byte[] epub, string cause)
    {
        var section = await factory.AddSectionAsync();
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var filesBefore = ListLibraryFiles();

        var job = await client.IngestAsync(epub, "book.epub", section.Id);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains(cause, job.Error);
        var failed = Assert.Single(job.Steps, s => s.Status == JobStepStatus.Failed);
        Assert.Equal("extract-metadata", failed.Name);
        Assert.Equal("extract-metadata", job.Steps[^1].Name);
        Assert.Equal((0, 0, 0), await factory.CountRowsAsync(section));
        Assert.Equal(filesBefore, ListLibraryFiles());
    }

    /// <summary>Every file and folder under the library folder, as paths relative to it, in order.</summary>
    private string[] ListLibraryFiles()
    {
        var folder = factory.LibraryPath();
        return Directory.Exists(folder)
            ? Directory.GetFileSystemEntries(folder, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(folder, path))
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];
    }

    /// <summary>The section's one work, as its detail page shows it.</summary>
    private static async Task<WorkDetailResponse> GetOnlyWorkAsync(HttpClient client, LibrarySection section) =>
        Assert.Single(await GetWorksAsync(client, section));

    /// <summary>Every work in the section, as its detail page shows it.</summary>
    private static async Task<List<WorkDetailResponse>> GetWorksAsync(HttpClient client, LibrarySection section)
    {
        var works = await client.GetAsync<Page<WorkSummaryResponse>>($"/api/works?sectionId={section.Id}");
        var details = new List<WorkDetailResponse>();
        foreach (var summary in works.Items)
        {
            details.Add(await client.GetAsync<WorkDetailResponse>($"/api/works/{summary.Id}"));
        }

        return details;
    }

    /// <summary>The work's detail as JSON with its items left out, for comparing everything else about it.</summary>
    private static string WithoutItems(WorkDetailResponse work) =>
        JsonSerializer.Serialize(work with { Items = [] }, ApiTestExtensions.Json);

    /// <summary>The name and sort name of each person, in the order the ids are given.</summary>
    private async Task<List<(string Name, string SortName)>> ReadPeopleAsync(IEnumerable<Guid> personIds)
    {
        var ids = personIds.ToList();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var people = await db.People.Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, TestContext.Current.CancellationToken);
        return ids.Select(id => (people[id].Name, people[id].SortName)).ToList();
    }

    /// <summary>The order of each of the work's contributors, lowest first.</summary>
    private async Task<List<int>> ReadContributorOrdersAsync(Guid workId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        return await db.WorkContributors.Where(c => c.WorkId == workId).OrderBy(c => c.Order)
            .Select(c => c.Order).ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Saves the row straight to the database, as one stored before ingest tidied names would be.</summary>
    private async Task SaveAsync(Entity row)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        db.Add(row);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
