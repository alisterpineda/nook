using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;

namespace Nook.Api.Tests.Integration;

public class ItemAndPartTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    private const string SharedSha256 = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    [Fact]
    public async Task Format_is_stored_as_the_enum_name()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var work = TestData.NewWork("Always Coming Home");
        var item = new Item { Format = ItemFormat.Epub };
        work.Items.Add(item);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.Works.Add(work);
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var stored = await db.Database
                .SqlQuery<string>($"SELECT format AS \"Value\" FROM items WHERE id = {item.Id}")
                .SingleAsync(cancellationToken);
            Assert.Equal("Epub", stored);
        }
    }

    [Fact]
    public async Task Work_can_hold_two_items_of_one_format()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var work = TestData.NewWork("The Word for World Is Forest");
        work.Items.Add(new Item { Format = ItemFormat.Epub });
        work.Items.Add(new Item { Format = ItemFormat.Epub });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.Works.Add(work);
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var count = await db.Items.CountAsync(i => i.WorkId == work.Id && i.Format == ItemFormat.Epub, cancellationToken);
            Assert.Equal(2, count);
        }
    }

    [Fact]
    public async Task Two_parts_can_share_a_sha256()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var work = TestData.NewWork("Rocannon's World");
        var first = new Item { Format = ItemFormat.Epub };
        var second = new Item { Format = ItemFormat.Epub };
        work.Items.Add(first);
        work.Items.Add(second);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.Works.Add(work);
            db.Parts.Add(NewPart(first.Id, "Rocannon's World/first.epub", SharedSha256));
            db.Parts.Add(NewPart(second.Id, "Rocannon's World/second.epub", SharedSha256));
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var items = await db.Parts
                .Where(p => p.Sha256 == SharedSha256)
                .Select(p => p.ItemId)
                .ToListAsync(cancellationToken);

            Assert.Equivalent(new[] { first.Id, second.Id }, items);
        }
    }

    [Fact]
    public async Task Parts_are_indexed_on_sha256()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();

        var definitions = await db.ReadIndexDefinitionsAsync("parts", cancellationToken);

        Assert.Contains(definitions, d => d.StartsWith("CREATE INDEX") && d.EndsWith("USING btree (sha256)"));
    }

    private static Part NewPart(Guid itemId, string path, string sha256) => new()
    {
        ItemId = itemId,
        Path = path,
        MediaType = "application/epub+zip",
        Size = 1024,
        Sha256 = sha256,
    };
}
