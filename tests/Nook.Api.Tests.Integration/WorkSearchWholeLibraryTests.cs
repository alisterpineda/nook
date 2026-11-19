using Nook.Api.Data;
using Nook.Api.Features;
using Nook.Api.Features.Works;

namespace Nook.Api.Tests.Integration;

/// <summary>A search of the whole library, which needs a database no other test adds works to.</summary>
public class WorkSearchWholeLibraryTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Search_returns_the_works_whose_title_has_the_word_and_counts_them()
    {
        var (weir, nagoski, kondo) = (
            new Person { Name = "Andy Weir", SortName = "Weir, Andy" },
            new Person { Name = "Emily Nagoski", SortName = "Nagoski, Emily" },
            new Person { Name = "Marie Kondo", SortName = "Kondo, Marie" });
        var (hail, come, life) = (
            TestData.NewWork("Project Hail Mary"),
            TestData.NewWork("Come as You Are"),
            TestData.NewWork("The Life-Changing Magic of Tidying Up"));
        hail.Contributors.Add(new WorkContributor { PersonId = weir.Id, Role = ContributorRole.Author });
        come.Contributors.Add(new WorkContributor { PersonId = nagoski.Id, Role = ContributorRole.Author });
        life.Contributors.Add(new WorkContributor { PersonId = kondo.Id, Role = ContributorRole.Author });
        await factory.AddSearchableRowsAsync(weir, nagoski, kondo, hail, come, life);
        using var client = await factory.CreateSignedInClientAsync();

        var page = await client.GetAsync<Page<WorkSummaryResponse>>("/api/works?Search=hail");

        Assert.Equal([hail.Id], page.Items.Select(w => w.Id));
        Assert.Equal(1, page.Total);
    }
}
