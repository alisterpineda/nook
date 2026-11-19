using System.Net;
using Nook.Api.Data;
using Nook.Api.Features;
using Nook.Api.Features.Works;
using Nook.Api.Features.PublicAccess;

namespace Nook.Api.Tests.Integration;

/// <summary>
/// Searching the works list. The class shares one database, so a test keeps other tests' works out by saving its
/// own in a section of its own and searching that section, or by using words no other test uses.
/// </summary>
public class WorkSearchTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Search_finds_a_work_by_its_author_its_series_or_its_tag()
    {
        var section = await factory.AddSectionAsync();
        var author = new Person { Name = "Zelda Authorson", SortName = "Authorson, Zelda" };
        var series = new Series { Name = "Seriesword Saga" };
        var tag = new Tag { Name = "Tagword" };
        var tagged = TestData.NewWork("Tagged Book", section.Id);
        var inSeries = TestData.NewWork("Serial Book", section.Id);
        inSeries.SeriesId = series.Id;
        var written = TestData.NewWork("Written Book", section.Id);
        written.Contributors.Add(new WorkContributor { PersonId = author.Id, Role = ContributorRole.Author });
        tagged.Tags.Add(tag);
        await factory.AddSearchableRowsAsync(author, series, tag, tagged, inSeries, written);
        using var client = await factory.CreateSignedInClientAsync();

        Assert.Equal([written.Id], await IdsAsync(client, "authorson", section));
        Assert.Equal([inSeries.Id], await IdsAsync(client, "seriesword", section));
        Assert.Equal([tagged.Id], await IdsAsync(client, "tagword", section));
    }

    [Fact]
    public async Task Search_ignores_the_description_the_publisher_the_external_ids_and_other_contributors()
    {
        var section = await factory.AddSectionAsync();
        var translator = new Person { Name = "Quentin Translatorson", SortName = "Translatorson, Quentin" };
        var work = TestData.NewWork("Plain Title", section.Id);
        work.Description = "Descriptionword";
        work.Publisher = "Publisherword";
        work.ExternalIds.Add(new WorkExternalId { Scheme = "isbn", Value = "9781234567897" });
        work.Contributors.Add(new WorkContributor { PersonId = translator.Id, Role = ContributorRole.Translator });
        await factory.AddSearchableRowsAsync(translator, work);
        using var client = await factory.CreateSignedInClientAsync();

        foreach (var term in new[] { "descriptionword", "publisherword", "9781234567897", "translatorson" })
        {
            Assert.Empty(await IdsAsync(client, term, section));
        }
        Assert.Equal([work.Id], await IdsAsync(client, "plain", section));
    }

    [Fact]
    public async Task Every_word_must_match_in_any_field_and_the_last_one_as_a_prefix()
    {
        var section = await factory.AddSectionAsync();
        var (weir, kondo) = (
            new Person { Name = "Andy Weir", SortName = "Weir, Andy" },
            new Person { Name = "Marie Kondo", SortName = "Kondo, Marie" });
        var hail = TestData.NewWork("Project Hail Mary", section.Id);
        var life = TestData.NewWork("The Life-Changing Magic of Tidying Up", section.Id);
        hail.Contributors.Add(new WorkContributor { PersonId = weir.Id, Role = ContributorRole.Author });
        life.Contributors.Add(new WorkContributor { PersonId = kondo.Id, Role = ContributorRole.Author });
        await factory.AddSearchableRowsAsync(weir, kondo, hail, life);
        using var client = await factory.CreateSignedInClientAsync();

        Assert.Equal([hail.Id], await IdsAsync(client, "hail ma", section));
        Assert.Equal([hail.Id], await IdsAsync(client, "weir hail", section));
        Assert.Empty(await IdsAsync(client, "hail kondo", section));
        Assert.Empty(await IdsAsync(client, "hai mary", section));
        Assert.Equal([life.Id], await IdsAsync(client, "the", section));
        Assert.Equal([life.Id], await IdsAsync(client, "tidy", section));
        Assert.Empty(await IdsAsync(client, "tidy up", section));
    }

    [Fact]
    public async Task Search_ignores_case_and_accents_on_both_sides()
    {
        var section = await factory.AddSectionAsync();
        var (accented, plain) = (
            new Person { Name = "Charlotte Brontë", SortName = "Brontë, Charlotte" },
            new Person { Name = "Bronte", SortName = "Bronte" });
        var (first, second) = (TestData.NewWork("First Title", section.Id), TestData.NewWork("Second Title", section.Id));
        first.Contributors.Add(new WorkContributor { PersonId = accented.Id, Role = ContributorRole.Author });
        second.Contributors.Add(new WorkContributor { PersonId = plain.Id, Role = ContributorRole.Author });
        await factory.AddSearchableRowsAsync(accented, plain, first, second);
        using var client = await factory.CreateSignedInClientAsync();

        Assert.Equal(await IdsAsync(client, "first", section), await IdsAsync(client, "FIRST", section));
        Assert.Equal([first.Id], await IdsAsync(client, "FIRST", section));
        Assert.Equivalent(new[] { first.Id, second.Id }, await IdsAsync(client, "bronte", section), strict: true);
        Assert.Equivalent(new[] { first.Id, second.Id }, await IdsAsync(client, "brontë", section), strict: true);
        Assert.Equivalent(new[] { first.Id, second.Id }, await IdsAsync(client, "bront", section), strict: true);
    }

    [Fact]
    public async Task Hyphenated_and_spaced_terms_find_the_same_work()
    {
        var section = await factory.AddSectionAsync();
        var work = TestData.NewWork("The Life-Changing Magic of Tidying Up", section.Id);
        await factory.AddSearchableRowsAsync(work);
        using var client = await factory.CreateSignedInClientAsync();

        Assert.Equal([work.Id], await IdsAsync(client, "life-changing", section));
        Assert.Equal([work.Id], await IdsAsync(client, "life changing", section));
    }

    [Fact]
    public async Task Word_with_combining_marks_is_found_by_itself()
    {
        var section = await factory.AddSectionAsync();
        var work = TestData.NewWork("हिन्दी", section.Id);
        await factory.AddSearchableRowsAsync(work);
        using var client = await factory.CreateSignedInClientAsync();

        Assert.Equal([work.Id], await IdsAsync(client, "हिन्दी", section));
    }

    [Fact]
    public async Task Apostrophe_separates_words()
    {
        var section = await factory.AddSectionAsync();
        var author = new Person { Name = "Flann O'Brien", SortName = "O'Brien, Flann" };
        var work = TestData.NewWork("The Third Policeman", section.Id);
        work.Contributors.Add(new WorkContributor { PersonId = author.Id, Role = ContributorRole.Author });
        await factory.AddSearchableRowsAsync(author, work);
        using var client = await factory.CreateSignedInClientAsync();

        Assert.Equal([work.Id], await IdsAsync(client, "o'brien", section));
        Assert.Equal([work.Id], await IdsAsync(client, "o brien", section));
        Assert.Equal([work.Id], await IdsAsync(client, "brien", section));
        Assert.Empty(await IdsAsync(client, "obrien", section));
    }

    [Theory]
    [InlineData("hail & | ! : * ( ) ' \"")]
    [InlineData("a:*")]
    [InlineData("\\")]
    [InlineData("'; drop table works; --")]
    public async Task Punctuation_and_operators_in_a_term_are_never_an_error(string term)
    {
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync(
            $"/api/works?search={Uri.EscapeDataString(term)}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Blank_term_is_no_term_and_a_term_of_only_punctuation_matches_nothing()
    {
        var section = await factory.AddSectionAsync();
        var (first, second) = (TestData.NewWork("Alpha", section.Id), TestData.NewWork("Beta", section.Id));
        await factory.AddSearchableRowsAsync(first, second);
        using var client = await factory.CreateSignedInClientAsync();

        var none = await client.GetAsync<Page<WorkSummaryResponse>>($"/api/works?sectionId={section.Id}");
        var blank = await client.GetAsync<Page<WorkSummaryResponse>>($"/api/works?sectionId={section.Id}&search=%20%20%20");
        var punctuation = await client.GetAsync<Page<WorkSummaryResponse>>(
            $"/api/works?sectionId={section.Id}&search={Uri.EscapeDataString("-- !!")}&offset=3&limit=7");

        Assert.Equal(none, blank, new PageComparer());
        Assert.Equal(2, blank.Total);
        Assert.Empty(punctuation.Items);
        Assert.Equal((0, 3, 7), (punctuation.Total, punctuation.Offset, punctuation.Limit));
    }

    [Fact]
    public async Task Term_longer_than_200_characters_is_refused()
    {
        using var client = await factory.CreateSignedInClientAsync();

        var at = await client.GetAsync($"/api/works?search={new string('a', 200)}", TestContext.Current.CancellationToken);
        var over = await client.GetAsync($"/api/works?search={new string('a', 201)}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, at.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
    }

    [Fact]
    public async Task Search_inside_a_section_or_the_default_area_returns_only_its_matches()
    {
        var section = await factory.AddSectionAsync();
        var word = $"zz{Guid.NewGuid():N}";
        var (inSection, inDefault, elsewhere) = (
            TestData.NewWork($"{word} in", section.Id),
            TestData.NewWork($"{word} out"),
            TestData.NewWork($"{word} other", (await factory.AddSectionAsync()).Id));
        await factory.AddSearchableRowsAsync(inSection, inDefault, elsewhere);
        using var client = await factory.CreateSignedInClientAsync();

        var ofSection = await client.GetAsync<Page<WorkSummaryResponse>>($"/api/works?search={word}&sectionId={section.Id}");
        var ofDefault = await client.GetAsync<Page<WorkSummaryResponse>>($"/api/works?search={word}&noSection=true");
        var ofAll = await client.GetAsync<Page<WorkSummaryResponse>>($"/api/works?search={word}");

        Assert.Equal([inSection.Id], ofSection.Items.Select(w => w.Id));
        Assert.Equal([inDefault.Id], ofDefault.Items.Select(w => w.Id));
        Assert.Equal(3, ofAll.Total);
    }

    [Fact]
    public async Task Visitor_search_returns_and_counts_only_what_public_access_opens()
    {
        var (open, closed) = (TestData.NewSection("Open"), TestData.NewSection("Closed"));
        var word = $"vv{Guid.NewGuid():N}";
        var (shown, hidden, inDefault) = (
            TestData.NewWork($"{word} shown", open.Id),
            TestData.NewWork($"{word} hidden", closed.Id),
            TestData.NewWork($"{word} default"));
        await factory.AddRowsAsync(open, closed);
        await factory.AddSearchableRowsAsync(shown, hidden, inDefault);
        await factory.SetPublicAccessAsync(
            true, ContentAccess.Download, ContentAccess.None, (open.Id, ContentAccess.Browse));
        using var visitor = factory.CreateClient();

        var page = await visitor.GetAsync<Page<WorkSummaryResponse>>($"/api/works?search={word}");

        Assert.Equal([shown.Id], page.Items.Select(w => w.Id));
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task Visitor_search_with_public_access_off_is_unauthorised()
    {
        await factory.SetPublicAccessAsync(false, ContentAccess.None);
        using var visitor = factory.CreateClient();

        var response = await visitor.GetAsync("/api/works?search=hail", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Match_in_the_title_comes_before_a_match_in_an_author()
    {
        var section = await factory.AddSectionAsync();
        var weir = new Person { Name = "Andy Weir", SortName = "Weir, Andy" };
        var other = new Person { Name = "Someone Else", SortName = "Else, Someone" };
        var hail = TestData.NewWork("Project Hail Mary", section.Id);
        hail.Contributors.Add(new WorkContributor { PersonId = weir.Id, Role = ContributorRole.Author });
        var titled = TestData.NewWork("Weir", section.Id, sortTitle: "z weir");
        titled.Contributors.Add(new WorkContributor { PersonId = other.Id, Role = ContributorRole.Author });
        await factory.AddSearchableRowsAsync(weir, other, hail, titled);
        using var client = await factory.CreateSignedInClientAsync();

        Assert.Equal([titled.Id, hail.Id], await IdsAsync(client, "weir", section));
    }

    [Fact]
    public async Task Works_that_rank_the_same_come_by_sort_title_and_page_without_repeats()
    {
        var section = await factory.AddSectionAsync();
        var author = new Person { Name = "Samename Author", SortName = "Author, Samename" };
        var works = new[] { "Charlie", "Alpha", "Bravo" }
            .Select(t => TestData.NewWork($"{t} book", section.Id))
            .ToArray();
        foreach (var work in works)
        {
            work.Contributors.Add(new WorkContributor { PersonId = author.Id, Role = ContributorRole.Author });
        }
        await factory.AddSearchableRowsAsync([author, .. works]);
        using var client = await factory.CreateSignedInClientAsync();

        var paged = new List<Guid>();
        for (var offset = 0; offset < 3; offset++)
        {
            var page = await client.GetAsync<Page<WorkSummaryResponse>>(
                $"/api/works?search=samename&sectionId={section.Id}&limit=1&offset={offset}");
            Assert.Equal(3, page.Total);
            paged.AddRange(page.Items.Select(w => w.Id));
        }

        Assert.Equal(works.OrderBy(w => w.SortTitle).Select(w => w.Id), paged);
    }

    [Fact]
    public async Task Search_with_no_sort_is_by_relevance_and_the_list_with_none_is_by_title()
    {
        var section = await factory.AddSectionAsync();
        var author = new Person { Name = "Rankword Author", SortName = "Author, Rankword" };
        var byAuthor = TestData.NewWork("Aaa", section.Id);
        byAuthor.Contributors.Add(new WorkContributor { PersonId = author.Id, Role = ContributorRole.Author });
        var byTitle = TestData.NewWork("Rankword", section.Id, sortTitle: "zzz");
        await factory.AddSearchableRowsAsync(author, byAuthor, byTitle);
        using var client = await factory.CreateSignedInClientAsync();

        var searched = await client.GetAsync<Page<WorkSummaryResponse>>(
            $"/api/works?search=rankword&sectionId={section.Id}");
        var listed = await client.GetAsync<Page<WorkSummaryResponse>>($"/api/works?sectionId={section.Id}");
        var explicitRelevance = await client.GetAsync<Page<WorkSummaryResponse>>(
            $"/api/works?search=rankword&sectionId={section.Id}&sort=Relevance");

        Assert.Equal([byTitle.Id, byAuthor.Id], searched.Items.Select(w => w.Id));
        Assert.Equal([byTitle.Id, byAuthor.Id], explicitRelevance.Items.Select(w => w.Id));
        Assert.Equal([byAuthor.Id, byTitle.Id], listed.Items.Select(w => w.Id));
    }

    [Fact]
    public async Task Search_sorted_by_title_or_added_comes_in_that_order()
    {
        var section = await factory.AddSectionAsync();
        var author = new Person { Name = "Sortword Author", SortName = "Author, Sortword" };
        var older = TestData.NewWork("Zed", section.Id);
        var newer = TestData.NewWork("Alpha", section.Id);
        var best = TestData.NewWork("Sortword", section.Id, sortTitle: "mmm");
        foreach (var work in new[] { older, newer })
        {
            work.Contributors.Add(new WorkContributor { PersonId = author.Id, Role = ContributorRole.Author });
        }
        // The work saved last is the newest.
        await factory.AddSearchableRowsAsync(author, best);
        await factory.AddSearchableRowsAsync(older);
        await factory.AddSearchableRowsAsync(newer);
        using var client = await factory.CreateSignedInClientAsync();

        var byTitle = await client.GetAsync<Page<WorkSummaryResponse>>(
            $"/api/works?search=sortword&sectionId={section.Id}&sort=Title");
        var byAdded = await client.GetAsync<Page<WorkSummaryResponse>>(
            $"/api/works?search=sortword&sectionId={section.Id}&sort=Added");

        Assert.Equal([newer.Id, best.Id, older.Id], byTitle.Items.Select(w => w.Id));
        Assert.Equal([newer.Id, older.Id, best.Id], byAdded.Items.Select(w => w.Id));
    }

    [Theory]
    [InlineData("/api/works?sort=Relevance")]
    [InlineData("/api/works?sort=Relevance&search=%20%20")]
    public async Task Relevance_with_no_term_is_refused(string url)
    {
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Work_with_no_vector_is_found_once_the_api_has_restarted()
    {
        var word = $"rr{Guid.NewGuid():N}";
        var work = TestData.NewWork($"{word} book");
        await factory.AddRowsAsync(work);

        // A second host over the same database stands for the API after a restart.
        await using var restarted = factory.WithWebHostBuilder(_ => { });
        using var client = await restarted.CreateSignedInClientAsync();

        var page = await client.GetAsync<Page<WorkSummaryResponse>>($"/api/works?search={word}");

        Assert.Equal([work.Id], page.Items.Select(w => w.Id));
    }

    private static async Task<List<Guid>> IdsAsync(HttpClient client, string term, LibrarySection section)
    {
        var page = await client.GetAsync<Page<WorkSummaryResponse>>(
            $"/api/works?search={Uri.EscapeDataString(term)}&sectionId={section.Id}&limit=200");
        return [.. page.Items.Select(w => w.Id)];
    }

    private sealed class PageComparer : IEqualityComparer<Page<WorkSummaryResponse>>
    {
        public bool Equals(Page<WorkSummaryResponse>? x, Page<WorkSummaryResponse>? y) =>
            x is not null && y is not null
            && (x.Total, x.Offset, x.Limit) == (y.Total, y.Offset, y.Limit)
            && x.Items.Select(w => w.Id).SequenceEqual(y.Items.Select(w => w.Id));

        public int GetHashCode(Page<WorkSummaryResponse> obj) => obj.Total;
    }
}
