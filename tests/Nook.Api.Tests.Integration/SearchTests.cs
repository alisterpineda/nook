using System.Net;
using Nook.Api.Data;
using Nook.Api.Features;
using Nook.Api.Features.Search;
using Nook.Api.Features.Works;

namespace Nook.Api.Tests.Integration;

/// <summary>
/// The grouped search. The class shares one database and the endpoint has no section filter, so every test searches
/// a word of its own and asserts only on its own works.
/// </summary>
public class SearchTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    private static string NewWord() => $"zz{Guid.NewGuid():N}";

    [Fact]
    public async Task Search_groups_matching_works_with_their_titles_and_authors()
    {
        var word = NewWord();
        var (adams, brown) = (
            new Person { Name = "Zed Adams", SortName = "Adams, Zed" },
            new Person { Name = "Amy Brown", SortName = "Brown, Amy" });
        var translator = new Person { Name = "Tia Translator", SortName = "Translator, Tia" };
        var (written, unwritten) = (TestData.NewWork($"{word} written"), TestData.NewWork($"{word} unwritten"));
        written.Contributors.Add(new WorkContributor { PersonId = adams.Id, Role = ContributorRole.Author, Order = 0 });
        written.Contributors.Add(new WorkContributor { PersonId = brown.Id, Role = ContributorRole.Author, Order = 1 });
        unwritten.Contributors.Add(
            new WorkContributor { PersonId = translator.Id, Role = ContributorRole.Translator });
        await factory.AddSearchableRowsAsync(adams, brown, translator, written, unwritten);
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync<SearchResponse>($"/api/search?Q={word}");

        var group = Assert.Single(response.Groups);
        Assert.Equal(SearchSource.Works, group.Source);
        Assert.Equal(2, group.Total);
        Assert.Equal("Zed Adams, Amy Brown", group.Results.Single(h => h.Id == written.Id).Subtitle);
        Assert.Equal($"{word} written", group.Results.Single(h => h.Id == written.Id).Title);
        Assert.Null(group.Results.Single(h => h.Id == unwritten.Id).Subtitle);
    }

    [Fact]
    public async Task Search_lists_works_in_the_works_lists_order_and_counts_those_past_the_limit()
    {
        var word = NewWord();
        var author = new Person { Name = $"Author {word}", SortName = $"{word}, Author" };
        var byAuthor = TestData.NewWork("Plain", sortTitle: "a plain");
        byAuthor.Contributors.Add(new WorkContributor { PersonId = author.Id, Role = ContributorRole.Author });
        var byTitle = TestData.NewWork($"{word} title", sortTitle: "z title");
        await factory.AddSearchableRowsAsync(author, byAuthor, byTitle);
        using var client = await factory.CreateSignedInClientAsync();

        var list = await client.GetAsync<Page<WorkSummaryResponse>>($"/api/works?search={word}");
        var all = await client.GetAsync<SearchResponse>($"/api/search?Q={word}");
        var limited = await client.GetAsync<SearchResponse>($"/api/search?Q={word}&Limit=1");

        Assert.Equal([byTitle.Id, byAuthor.Id], list.Items.Select(w => w.Id));
        Assert.Equal(list.Items.Select(w => w.Id), all.Groups.Single().Results.Select(h => h.Id));
        var group = Assert.Single(limited.Groups);
        Assert.Equal([byTitle.Id], group.Results.Select(h => h.Id));
        Assert.Equal(2, group.Total);
    }

    [Fact]
    public async Task Search_matches_every_word_with_the_last_as_a_prefix()
    {
        var word = NewWord();
        var work = TestData.NewWork($"{word} Hail");
        await factory.AddSearchableRowsAsync(work);
        using var client = await factory.CreateSignedInClientAsync();

        var found = await client.GetAsync<SearchResponse>($"/api/search?Q={word}%20hai");
        var missed = await client.GetAsync<SearchResponse>($"/api/search?Q={word}%20hai%20mary");

        Assert.Equal([work.Id], found.Groups.Single().Results.Select(h => h.Id));
        Assert.Empty(missed.Groups);
    }

    [Fact]
    public async Task Search_returns_six_works_when_no_limit_is_given()
    {
        var word = NewWord();
        var works = Enumerable.Range(0, 7).Select(i => TestData.NewWork($"{word} {i}")).ToArray<object>();
        await factory.AddSearchableRowsAsync(works);
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync<SearchResponse>($"/api/search?Q={word}");

        var group = Assert.Single(response.Groups);
        Assert.Equal(6, group.Results.Count);
        Assert.Equal(7, group.Total);
    }

    [Fact]
    public async Task Search_leaves_out_a_source_with_no_match()
    {
        using var client = await factory.CreateSignedInClientAsync();

        var unused = await client.GetAsync<SearchResponse>($"/api/search?Q={NewWord()}");
        var noWords = await client.GetAsync<SearchResponse>("/api/search?Q=!!");

        Assert.Empty(unused.Groups);
        Assert.Empty(noWords.Groups);
    }

    [Fact]
    public async Task Visitor_search_returns_and_counts_only_what_public_access_opens()
    {
        var (open, closed) = (TestData.NewSection("Open"), TestData.NewSection("Closed"));
        var word = NewWord();
        var (shown, hidden, inDefault) = (
            TestData.NewWork($"{word} shown", open.Id),
            TestData.NewWork($"{word} hidden", closed.Id),
            TestData.NewWork($"{word} default"));
        await factory.AddRowsAsync(open, closed);
        await factory.AddSearchableRowsAsync(shown, hidden, inDefault);
        await factory.SetPublicAccessAsync(
            true, ContentAccess.Download, ContentAccess.None, (open.Id, ContentAccess.Browse));
        using var visitor = factory.CreateClient();

        var response = await visitor.GetAsync<SearchResponse>($"/api/search?Q={word}");

        var group = Assert.Single(response.Groups);
        Assert.Equal([shown.Id], group.Results.Select(h => h.Id));
        Assert.Equal(1, group.Total);
    }

    [Fact]
    public async Task Visitor_search_with_public_access_off_is_unauthorised()
    {
        await factory.SetPublicAccessAsync(false, ContentAccess.None);
        using var visitor = factory.CreateClient();

        var response = await visitor.GetAsync("/api/search?Q=hail", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?Q=")]
    [InlineData("?Q=a")]
    [InlineData("?Q=%20a%20")]
    public async Task Search_refuses_a_term_shorter_than_two_characters(string query)
    {
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/search{query}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_refuses_a_term_longer_than_200_characters()
    {
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync(
            $"/api/search?Q={new string('a', 201)}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task Search_refuses_a_limit_outside_1_to_20(int limit)
    {
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/search?Q=hail&Limit={limit}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_accepts_a_two_character_term()
    {
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync("/api/search?Q=ab", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
