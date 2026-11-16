using System.Net;
using Nook.Api.Data;
using Nook.Api.Features;
using Nook.Api.Features.People;
using Nook.Api.Features.Works;

namespace Nook.Api.Tests.Integration;

public class PeopleApiTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task People_are_listed_by_sort_name_with_their_work_counts()
    {
        var ann = new Person { Name = "Ann Zimmer", SortName = "Zimmer, Ann" };
        var zoe = new Person { Name = "Zoe Abbott", SortName = "Abbott, Zoe" };
        var uncredited = new Person { Name = "Una Credited", SortName = "Credited, Una" };
        var first = TestData.NewWork("First");
        first.Contributors.Add(new WorkContributor { PersonId = ann.Id });
        var second = TestData.NewWork("Second");
        second.Contributors.Add(new WorkContributor { PersonId = ann.Id });
        var both = TestData.NewWork("Both");
        both.Contributors.Add(new WorkContributor { PersonId = zoe.Id, Order = 0 });
        both.Contributors.Add(new WorkContributor { PersonId = zoe.Id, Order = 1, Role = ContributorRole.Translator });
        await factory.AddRowsAsync(ann, zoe, uncredited, first, second, both);
        using var client = await factory.CreateSignedInClientAsync();

        var page = await client.GetAsync<Page<PersonSummaryResponse>>("/api/people?limit=200");

        // Other tests in this class add people to the same database, so only these three are judged.
        Assert.Equal(
            [
                new PersonSummaryResponse(zoe.Id, "Zoe Abbott", "Abbott, Zoe", 1),
                new PersonSummaryResponse(ann.Id, "Ann Zimmer", "Zimmer, Ann", 2),
            ],
            page.Items.Where(p => p.Id == ann.Id || p.Id == zoe.Id || p.Id == uncredited.Id));
    }

    [Fact]
    public async Task People_can_be_listed_for_one_role()
    {
        var writer = new Person { Name = "Wren Writer", SortName = "Writer, Wren" };
        var translator = new Person { Name = "Tam Translator", SortName = "Translator, Tam" };
        var one = TestData.NewWork("One");
        one.Contributors.Add(new WorkContributor { PersonId = writer.Id });
        var two = TestData.NewWork("Two");
        two.Contributors.Add(new WorkContributor { PersonId = writer.Id });
        var translated = TestData.NewWork("Translated");
        translated.Contributors.Add(new WorkContributor { PersonId = writer.Id, Order = 0, Role = ContributorRole.Translator });
        translated.Contributors.Add(new WorkContributor { PersonId = translator.Id, Order = 1, Role = ContributorRole.Translator });
        await factory.AddRowsAsync(writer, translator, one, two, translated);
        using var client = await factory.CreateSignedInClientAsync();

        var translators = await client.GetAsync<Page<PersonSummaryResponse>>("/api/people?role=translator&limit=200");
        var authors = await client.GetAsync<Page<PersonSummaryResponse>>("/api/people?role=author&limit=200");

        Assert.Equal(
            [(translator.Id, 1), (writer.Id, 1)],
            translators.Items.Where(p => p.Id == writer.Id || p.Id == translator.Id).Select(p => (p.Id, p.WorkCount)));
        Assert.Equal(
            [(writer.Id, 2)],
            authors.Items.Where(p => p.Id == writer.Id || p.Id == translator.Id).Select(p => (p.Id, p.WorkCount)));
    }

    [Theory]
    [InlineData("role=nobody")]
    [InlineData("offset=-1")]
    [InlineData("limit=0")]
    [InlineData("limit=201")]
    public async Task People_rejects_a_bad_query(string query)
    {
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/people?{query}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Person_shows_their_works_by_role_then_series()
    {
        var person = new Person { Name = "Pat Writer", SortName = "Writer, Pat" };
        var other = new Person { Name = "Oda Other", SortName = "Other, Oda" };
        var zeta = new Series { Name = "Zeta" };
        var alpha = new Series { Name = "Alpha" };
        var zetaTwo = AuthoredBy(person, "Zeta Two", zeta, 2m);
        var zetaOne = AuthoredBy(person, "Zeta One", zeta, 1m);
        var alphaUnplaced = AuthoredBy(person, "Alpha Unplaced", alpha, null);
        var alphaOne = AuthoredBy(person, "Alpha One", alpha, 1m);
        var alone = AuthoredBy(person, "Alone", null, null);
        var translated = TestData.NewWork("Translated");
        translated.Contributors.Add(new WorkContributor { PersonId = other.Id, Order = 0 });
        translated.Contributors.Add(new WorkContributor { PersonId = person.Id, Order = 1, Role = ContributorRole.Translator });
        await factory.AddRowsAsync(
            person, other, zeta, alpha, zetaTwo, zetaOne, alphaUnplaced, alphaOne, alone, translated);
        using var client = await factory.CreateSignedInClientAsync();

        var detail = await client.GetAsync<PersonDetailResponse>($"/api/people/{person.Id}");

        Assert.Equal(person.Id, detail.Id);
        Assert.Equal("Pat Writer", detail.Name);
        Assert.Equal("Writer, Pat", detail.SortName);
        Assert.Equal([ContributorRole.Author, ContributorRole.Translator], detail.Roles.Select(r => r.Role));
        Assert.Equal(
            [alphaOne.Id, alphaUnplaced.Id, zetaOne.Id, zetaTwo.Id, alone.Id],
            detail.Roles[0].Works.Select(w => w.Id));
        Assert.Equal([translated.Id], detail.Roles[1].Works.Select(w => w.Id));
    }

    [Fact]
    public async Task Person_work_has_the_same_shape_as_in_the_works_list()
    {
        var section = await factory.AddSectionAsync();
        var person = new Person { Name = "Sam Shape", SortName = "Shape, Sam" };
        var coauthor = new Person { Name = "Cy Coauthor", SortName = "Coauthor, Cy" };
        var series = new Series { Name = "Shapes" };
        var work = TestData.NewWork("Shaped", section.Id);
        work.SeriesId = series.Id;
        work.SeriesPosition = 3m;
        work.CoverHash = "shape-hash";
        work.Contributors.Add(new WorkContributor { PersonId = person.Id, Order = 0 });
        work.Contributors.Add(new WorkContributor { PersonId = coauthor.Id, Order = 1 });
        work.Items.Add(new Item { Format = ItemFormat.Epub });
        await factory.AddRowsAsync(person, coauthor, series, work);
        using var client = await factory.CreateSignedInClientAsync();

        var listed = await client.GetAsync<Page<WorkSummaryResponse>>($"/api/works?sectionId={section.Id}");
        var detail = await client.GetAsync<PersonDetailResponse>($"/api/people/{person.Id}");

        var expected = Assert.Single(listed.Items);
        var role = Assert.Single(detail.Roles);
        var actual = Assert.Single(role.Works);
        // The record's own equality compares its lists by reference, so the values are compared one by one.
        Assert.Equivalent(expected, actual, strict: true);
    }

    [Fact]
    public async Task Person_with_two_works_in_no_series_lists_them_by_sort_title()
    {
        var person = new Person { Name = "Nia Noseries", SortName = "Noseries, Nia" };
        var zebra = AuthoredBy(person, "A Zebra", null, null, "Zebra, A");
        var buffalo = AuthoredBy(person, "Buffalo", null, null);
        await factory.AddRowsAsync(person, zebra, buffalo);
        using var client = await factory.CreateSignedInClientAsync();

        var detail = await client.GetAsync<PersonDetailResponse>($"/api/people/{person.Id}");

        var role = Assert.Single(detail.Roles);
        Assert.Equal(["Buffalo", "A Zebra"], role.Works.Select(w => w.Title));
    }

    [Fact]
    public async Task Person_credited_on_no_work_is_not_found()
    {
        var person = new Person { Name = "Una Uncredited", SortName = "Uncredited, Una" };
        await factory.AddRowsAsync(person);
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/people/{person.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unknown_person_is_not_found()
    {
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/people/{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>A work the person is the only author of, in the given series at the given place.</summary>
    private static Work AuthoredBy(
        Person person, string title, Series? series, decimal? position, string? sortTitle = null)
    {
        var work = TestData.NewWork(title, sortTitle: sortTitle);
        work.SeriesId = series?.Id;
        work.SeriesPosition = position;
        work.Contributors.Add(new WorkContributor { PersonId = person.Id });
        return work;
    }
}
