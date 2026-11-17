using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features;
using Nook.Api.Features.Sections;
using Nook.Api.Features.Works;

namespace Nook.Api.Tests.Integration;

public class CatalogApiTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Sections_lists_the_sections_by_name()
    {
        var zebra = await factory.AddSectionAsync("Zebra");
        var apple = await factory.AddSectionAsync("Apple");
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Viewer);

        var sections = await client.GetAsync<List<SectionResponse>>("/api/sections");

        // Other tests in this class add sections to the same database, so only these two are judged.
        Assert.Equal(
            [new SectionResponse(apple.Id, "Apple"), new SectionResponse(zebra.Id, "Zebra")],
            sections.Where(s => s.Id == zebra.Id || s.Id == apple.Id));
        var gone = await client.GetAsync("/api/libraries", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Works_come_in_pages_by_sort_title()
    {
        var section = await AddSectionAsync(
            sectionId => [TestData.NewWork("The Dispossessed", sectionId, "Dispossessed, The"), TestData.NewWork("Emma", sectionId, "Emma"), TestData.NewWork("Carrie", sectionId, "Carrie")]);
        using var client = await factory.CreateSignedInClientAsync();

        var first = await client.GetAsync<Page<WorkSummaryResponse>>($"/api/works?sectionId={section}&limit=2");
        var second = await client.GetAsync<Page<WorkSummaryResponse>>($"/api/works?sectionId={section}&limit=2&offset=2");

        Assert.Equal(["Carrie", "The Dispossessed"], first.Items.Select(w => w.Title));
        Assert.Equal((3, 0, 2), (first.Total, first.Offset, first.Limit));
        Assert.Equal(["Emma"], second.Items.Select(w => w.Title));
        Assert.Equal((3, 2, 2), (second.Total, second.Offset, second.Limit));
    }

    [Fact]
    public async Task Works_sorted_by_added_come_newest_first()
    {
        var section = await AddSectionAsync(sectionId => [TestData.NewWork("First", sectionId, "First")]);
        await factory.AddRowsAsync(TestData.NewWork("Second", section, "Second"));
        await factory.AddRowsAsync(TestData.NewWork("Third", section, "Third"));
        using var client = await factory.CreateSignedInClientAsync();

        var page = await client.GetAsync<Page<WorkSummaryResponse>>($"/api/works?sectionId={section}&sort=added");

        Assert.Equal(["Third", "Second", "First"], page.Items.Select(w => w.Title));
        Assert.Equal(50, page.Limit);
    }

    [Fact]
    public async Task Work_in_a_list_shows_its_authors_series_and_formats()
    {
        var series = new Series { Name = "Hainish Cycle" };
        var ursula = new Person { Name = "Ursula K. Le Guin", SortName = "Le Guin, Ursula K." };
        var coauthor = new Person { Name = "A. Coauthor", SortName = "Coauthor, A." };
        var translator = new Person { Name = "T. Translator", SortName = "Translator, T." };
        Work? work = null;
        var section = await AddSectionAsync(sectionId =>
        {
            work = TestData.NewWork("The Word for World Is Forest", sectionId, "Word for World Is Forest, The");
            work.SeriesId = series.Id;
            work.SeriesPosition = 2.5m;
            work.CoverHash = "abc123";
            work.Contributors.Add(new WorkContributor { PersonId = coauthor.Id, Order = 1 });
            work.Contributors.Add(new WorkContributor { PersonId = ursula.Id, Order = 0 });
            work.Contributors.Add(new WorkContributor { PersonId = translator.Id, Order = 2, Role = ContributorRole.Translator });
            work.Items.Add(new Item { Format = ItemFormat.Epub });
            work.Items.Add(new Item { Format = ItemFormat.Epub });
            return [series, ursula, coauthor, translator, work];
        });
        using var client = await factory.CreateSignedInClientAsync();

        var page = await client.GetAsync<Page<WorkSummaryResponse>>($"/api/works?sectionId={section}");

        var summary = Assert.Single(page.Items);
        Assert.Equal(work!.Id, summary.Id);
        Assert.Equal(section, summary.SectionId);
        Assert.Equal(["Ursula K. Le Guin", "A. Coauthor"], summary.Authors);
        Assert.Equal(new WorkSeriesResponse(series.Id, "Hainish Cycle", 2.5m), summary.Series);
        Assert.Equal("abc123", summary.CoverHash);
        Assert.Equal([ItemFormat.Epub], summary.Formats);

        var raw = await client.GetStringAsync($"/api/works?sectionId={section}", TestContext.Current.CancellationToken);
        Assert.Contains("\"formats\":[\"epub\"]", raw);
    }

    [Fact]
    public async Task Work_with_nothing_attached_lists_with_empty_values()
    {
        var section = await AddSectionAsync(sectionId => [TestData.NewWork("Bare", sectionId, "Bare")]);
        using var client = await factory.CreateSignedInClientAsync();

        var page = await client.GetAsync<Page<WorkSummaryResponse>>($"/api/works?sectionId={section}");

        var summary = Assert.Single(page.Items);
        Assert.Empty(summary.Authors);
        Assert.Null(summary.Series);
        Assert.Null(summary.CoverHash);
        Assert.Empty(summary.Formats);
    }

    [Fact]
    public async Task Works_can_be_listed_for_one_section_or_for_the_default_area()
    {
        var title = $"Sectioned or not {Guid.NewGuid():N}";
        var section = await factory.AddSectionAsync();
        var inSection = TestData.NewWork(title, section.Id);
        var inNone = TestData.NewWork(title);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.Works.AddRange(inSection, inNone);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var client = await factory.CreateSignedInClientAsync();

        var ofSection = await client.GetAsync<Page<WorkSummaryResponse>>($"/api/works?sectionId={section.Id}&limit=200");
        var ofDefault = await client.GetAsync<Page<WorkSummaryResponse>>("/api/works?noSection=true&limit=200");
        var all = await client.GetAsync<Page<WorkSummaryResponse>>("/api/works?limit=200");

        // Other tests in this class add works to the same database, so only the two with this title are judged.
        Assert.Equal([(inSection.Id, (Guid?)section.Id)], ofSection.Items.Select(w => (w.Id, w.SectionId)));
        Assert.Equal(
            [(inNone.Id, (Guid?)null)],
            ofDefault.Items.Where(w => w.Title == title).Select(w => (w.Id, w.SectionId)));
        Assert.All(ofDefault.Items, w => Assert.Null(w.SectionId));
        Assert.Equal(
            new[] { inSection.Id, inNone.Id }.Order(),
            all.Items.Where(w => w.Title == title).Select(w => w.Id).Order());
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=201")]
    [InlineData("offset=-1")]
    [InlineData("sort=sideways")]
    [InlineData("sectionId=not-a-guid")]
    [InlineData("sectionId=01a1c7e7-8000-7000-8000-0000000000aa&noSection=true")]
    public async Task Works_rejects_a_bad_query(string query)
    {
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/works?{query}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Work_detail_returns_everything_about_the_work()
    {
        var series = new Series { Name = "Earthsea" };
        var ursula = new Person { Name = "Ursula K. Le Guin", SortName = "Le Guin, Ursula K." };
        var narrator = new Person { Name = "N. Narrator", SortName = "Narrator, N." };
        var fantasy = new Tag { Name = "Fantasy" };
        var classic = new Tag { Name = "Classic" };
        var item = new Item { Format = ItemFormat.Epub };
        var section = await factory.AddSectionAsync();
        var work = new Work
        {
            SectionId = section.Id,
            Title = "A Wizard of Earthsea",
            SortTitle = "Wizard of Earthsea, A",
            Description = "Ged goes to Roke.",
            Language = "en",
            PublicationDate = new DateOnly(1968, 11, 1),
            Publisher = "Parnassus Press",
            SeriesId = series.Id,
            SeriesPosition = 1m,
            CoverHash = "cover-hash",
        };
        work.Contributors.Add(new WorkContributor { PersonId = narrator.Id, Order = 1, Role = ContributorRole.Narrator });
        work.Contributors.Add(new WorkContributor { PersonId = ursula.Id, Order = 0 });
        work.Tags.Add(fantasy);
        work.Tags.Add(classic);
        work.ExternalIds.Add(new WorkExternalId { Scheme = "isbn", Value = "9780547773742" });
        work.Items.Add(item);
        var second = new Part { ItemId = item.Id, Order = 1, Path = "b.epub", MediaType = "application/epub+zip", Size = 20, Sha256 = new string('b', 64) };
        var first = new Part { ItemId = item.Id, Order = 0, Path = "a.epub", MediaType = "application/epub+zip", Size = 10, Sha256 = new string('a', 64) };
        await factory.AddRowsAsync(series, ursula, narrator, work, second, first);
        using var client = await factory.CreateSignedInClientAsync();

        var detail = await client.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}");

        Assert.Equal(work.Id, detail.Id);
        Assert.Equal(section.Id, detail.SectionId);
        Assert.Equal("A Wizard of Earthsea", detail.Title);
        Assert.Equal("Wizard of Earthsea, A", detail.SortTitle);
        Assert.Equal("Ged goes to Roke.", detail.Description);
        Assert.Equal("en", detail.Language);
        Assert.Equal(new DateOnly(1968, 11, 1), detail.PublicationDate);
        Assert.Equal("Parnassus Press", detail.Publisher);
        Assert.Equal(new WorkSeriesResponse(series.Id, "Earthsea", 1m), detail.Series);
        Assert.Equal("cover-hash", detail.CoverHash);
        Assert.Equal(
            [
                new WorkContributorResponse(ursula.Id, "Ursula K. Le Guin", ContributorRole.Author),
                new WorkContributorResponse(narrator.Id, "N. Narrator", ContributorRole.Narrator),
            ],
            detail.Contributors);
        Assert.Equal([new WorkTagResponse(classic.Id, "Classic"), new WorkTagResponse(fantasy.Id, "Fantasy")], detail.Tags);
        Assert.Equal([new WorkExternalIdResponse("isbn", "9780547773742")], detail.ExternalIds);
        var detailItem = Assert.Single(detail.Items);
        Assert.Equal(item.Id, detailItem.Id);
        Assert.Equal(ItemFormat.Epub, detailItem.Format);
        Assert.Equal(
            [
                new WorkPartResponse(first.Id, "application/epub+zip", 10),
                new WorkPartResponse(second.Id, "application/epub+zip", 20),
            ],
            detailItem.Parts);
        Assert.NotEqual(default, detail.AddedAt);

        // Where a file sits on disk stays inside Nook.
        var raw = await client.GetStringAsync($"/api/works/{work.Id}", TestContext.Current.CancellationToken);
        Assert.DoesNotContain("a.epub", raw);
    }

    [Fact]
    public async Task Work_detail_of_a_bare_work_has_empty_lists()
    {
        var work = TestData.NewWork("Bare detail");
        await factory.AddRowsAsync(work);
        using var client = await factory.CreateSignedInClientAsync();

        var detail = await client.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}");

        Assert.Null(detail.Series);
        Assert.Empty(detail.Contributors);
        Assert.Empty(detail.Tags);
        Assert.Empty(detail.ExternalIds);
        Assert.Empty(detail.Items);
    }

    [Fact]
    public async Task Work_detail_of_an_unknown_work_is_not_found()
    {
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/works/{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>Adds a section of its own for one test, so works from other tests stay out of its lists.</summary>
    private async Task<Guid> AddSectionAsync(Func<Guid, object[]> rows)
    {
        var section = await factory.AddSectionAsync();
        await factory.AddRowsAsync(rows(section.Id));
        return section.Id;
    }
}
