using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features.Works;

namespace Nook.Api.Tests.Integration;

public class WorkSearchIndexTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Vector_holds_the_title_the_authors_the_series_and_the_tags_each_at_its_weight()
    {
        var author = new Person { Name = "Zelda Authorson", SortName = "Authorson, Zelda" };
        var translator = new Person { Name = "Quentin Translatorson", SortName = "Translatorson, Quentin" };
        var series = new Series { Name = "Seriesword Saga" };
        var tag = new Tag { Name = "Tagword" };
        var work = new Work
        {
            Title = "Titleword Tale",
            SortTitle = "Titleword Tale",
            SeriesId = series.Id,
            Description = "Descriptionword",
            Publisher = "Publisherword",
        };
        work.Contributors.Add(new WorkContributor { PersonId = author.Id, Role = ContributorRole.Author });
        work.Contributors.Add(new WorkContributor { PersonId = translator.Id, Role = ContributorRole.Translator, Order = 1 });
        work.Tags.Add(tag);
        await factory.AddRowsAsync(author, translator, series, work);

        var vector = await RebuildAndReadAsync(work.Id);

        Assert.Contains("'titleword':1A", vector);
        Assert.Contains("'tale':2A", vector);
        Assert.Contains("'zelda':3B", vector);
        Assert.Contains("'authorson':4B", vector);
        Assert.Matches("'seriesword':\\d+C", vector);
        Assert.Matches("'saga':\\d+C", vector);
        Assert.Matches("'tagword':\\d+C", vector);
        Assert.DoesNotContain("quentin", vector);
        Assert.DoesNotContain("translatorson", vector);
        Assert.DoesNotContain("descriptionword", vector);
        Assert.DoesNotContain("publisherword", vector);
    }

    [Fact]
    public async Task Vector_of_a_hyphenated_title_holds_its_parts_as_words()
    {
        var work = TestData.NewWork("Life-Changing");
        await factory.AddRowsAsync(work);

        var vector = await RebuildAndReadAsync(work.Id);

        Assert.Contains("'life'", vector);
        Assert.Contains("'changing'", vector);
        Assert.DoesNotContain("life-changing", vector);
    }

    private async Task<string> RebuildAndReadAsync(Guid workId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        await new WorkSearchIndex(db).RebuildAsync([workId], cancellationToken);
        return await db.Database
            .SqlQuery<string>($"SELECT search_vector::text AS \"Value\" FROM works WHERE id = {workId}")
            .SingleAsync(cancellationToken);
    }
}
