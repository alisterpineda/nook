using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;

namespace Nook.Api.Tests.Integration;

/// <summary>Adding to a work that was loaded from the database, the way ingest adds to a work it already holds.</summary>
public class TrackedWorkTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task New_children_added_to_a_loaded_work_are_inserted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var work = TestData.NewWork("Lavinia");
        var person = new Person { Name = "Ursula K. Le Guin", SortName = "Le Guin, Ursula K." };

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.AddRange(work, person);
            await db.SaveChangesAsync(cancellationToken);
        }

        var item = new Item { Format = ItemFormat.Epub };
        var externalId = new WorkExternalId { Scheme = "isbn", Value = "9780151014248" };
        var contributor = new WorkContributor { PersonId = person.Id };
        var tag = new Tag { Name = "Historical" };

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var loaded = await db.Works.SingleAsync(w => w.Id == work.Id, cancellationToken);
            loaded.Items.Add(item);
            loaded.ExternalIds.Add(externalId);
            loaded.Contributors.Add(contributor);
            loaded.Tags.Add(tag);
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var savedItem = await db.Items.SingleAsync(i => i.Id == item.Id, cancellationToken);

            Assert.Equal(work.Id, savedItem.WorkId);
            Assert.NotEqual(default, savedItem.CreatedAt);
            Assert.True(await db.WorkExternalIds.AnyAsync(e => e.Id == externalId.Id && e.WorkId == work.Id, cancellationToken));
            Assert.True(await db.WorkContributors.AnyAsync(c => c.Id == contributor.Id && c.WorkId == work.Id, cancellationToken));
            Assert.True(await db.Works.AnyAsync(w => w.Id == work.Id && w.Tags.Any(t => t.Id == tag.Id), cancellationToken));
        }
    }
}
