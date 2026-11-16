using Nook.Api.Data;
using Nook.Api.Features;
using Nook.Api.Features.People;

namespace Nook.Api.Tests.Integration;

/// <summary>A class of its own, so its database holds only the people these tests add.</summary>
public class PeoplePagingTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task People_come_in_pages()
    {
        var carrie = new Person { Name = "Carrie Cole", SortName = "Cole, Carrie" };
        var abe = new Person { Name = "Abe Adams", SortName = "Adams, Abe" };
        var bea = new Person { Name = "Bea Brown", SortName = "Brown, Bea" };
        var rows = new List<object> { carrie, abe, bea };
        foreach (var person in new[] { carrie, abe, bea })
        {
            var work = TestData.NewWork($"By {person.Name}");
            work.Contributors.Add(new WorkContributor { PersonId = person.Id });
            rows.Add(work);
        }
        await factory.AddRowsAsync([.. rows]);
        using var client = await factory.CreateSignedInClientAsync();

        var first = await client.GetAsync<Page<PersonSummaryResponse>>("/api/people?limit=2");
        var second = await client.GetAsync<Page<PersonSummaryResponse>>("/api/people?limit=2&offset=2");
        var unasked = await client.GetAsync<Page<PersonSummaryResponse>>("/api/people");

        Assert.Equal([abe.Id, bea.Id], first.Items.Select(p => p.Id));
        Assert.Equal((3, 0, 2), (first.Total, first.Offset, first.Limit));
        Assert.Equal([carrie.Id], second.Items.Select(p => p.Id));
        Assert.Equal((3, 2, 2), (second.Total, second.Offset, second.Limit));
        Assert.Equal((3, 0, 50), (unasked.Total, unasked.Offset, unasked.Limit));
    }
}
