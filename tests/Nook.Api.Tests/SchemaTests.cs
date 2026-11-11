using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;

namespace Nook.Api.Tests;

/// <summary>Pins the whole schema and the cascade from a work.</summary>
public class SchemaTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    private static readonly string[] IdentityChildTables =
        ["asp_net_user_claims", "asp_net_user_logins", "asp_net_user_tokens"];

    private static readonly string[] NookTables =
    [
        "asp_net_users", "libraries", "works", "items", "parts", "people", "work_contributors", "series", "tags",
        "work_external_ids", "ingest_runs", "item_steps",
    ];

    [Fact]
    public async Task Database_has_exactly_the_expected_tables()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();

        var tables = await db.Database
            .SqlQuery<string>($"SELECT table_name::text AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'")
            .ToListAsync(cancellationToken);

        string[] expected = [.. NookTables, .. IdentityChildTables, "work_tags", "__EFMigrationsHistory"];
        Assert.Equal(expected.Order(), tables.Order());
        Assert.DoesNotContain(tables, t =>
            t.Contains("role") || t.Contains("grant") || t.Contains("progress") || t.Contains("state") || t.Contains("shel"));
    }

    [Fact]
    public async Task Every_nook_table_has_a_uuid_id_and_both_timestamps()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();

        var columns = await db.Database
            .SqlQuery<string>($"SELECT (table_name || '.' || column_name || ' ' || udt_name || ' ' || is_nullable)::text AS \"Value\" FROM information_schema.columns WHERE table_schema = 'public'")
            .ToListAsync(cancellationToken);

        Assert.All(NookTables, table =>
        {
            Assert.Contains($"{table}.id uuid NO", columns);
            Assert.Contains($"{table}.created_at timestamptz NO", columns);
            Assert.Contains($"{table}.updated_at timestamptz NO", columns);
        });
    }

    [Fact]
    public async Task Deleting_a_work_removes_everything_under_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var person = new Person { Name = "Ursula K. Le Guin", SortName = "Le Guin, Ursula K." };
        var series = new Series { Name = "Hainish Cycle" };
        var tag = new Tag { Name = "Science fiction" };
        var item = new Item { Format = ItemFormat.Epub };
        var work = new Work
        {
            LibraryId = Library.DefaultId,
            Title = "The Dispossessed",
            SortTitle = "Dispossessed, The",
            SeriesId = series.Id,
            SeriesPosition = 6m,
        };
        work.Items.Add(item);
        work.Contributors.Add(new WorkContributor { PersonId = person.Id });
        work.Tags.Add(tag);
        work.ExternalIds.Add(new WorkExternalId { Scheme = "isbn", Value = "9780060512750" });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.AddRange(person, series, work);
            db.Parts.Add(new Part
            {
                ItemId = item.Id,
                Path = "The Dispossessed/book.epub",
                MediaType = "application/epub+zip",
                Size = 1024,
                Sha256 = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
            });
            db.ItemSteps.Add(new ItemStep { ItemId = item.Id, StepName = "extract-metadata", StepVersion = 1 });
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            Assert.Equal(1, await db.CountTagLinksAsync(work.Id, cancellationToken));

            // Deleted without loading its children, so the database's own delete rules do the work.
            var tracked = await db.Works.SingleAsync(w => w.Id == work.Id, cancellationToken);
            db.Works.Remove(tracked);
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();

            Assert.False(await db.Works.AnyAsync(w => w.Id == work.Id, cancellationToken));
            Assert.False(await db.Items.AnyAsync(i => i.WorkId == work.Id, cancellationToken));
            Assert.False(await db.Parts.AnyAsync(p => p.ItemId == item.Id, cancellationToken));
            Assert.False(await db.ItemSteps.AnyAsync(s => s.ItemId == item.Id, cancellationToken));
            Assert.False(await db.WorkContributors.AnyAsync(c => c.WorkId == work.Id, cancellationToken));
            Assert.False(await db.WorkExternalIds.AnyAsync(e => e.WorkId == work.Id, cancellationToken));
            Assert.Equal(0, await db.CountTagLinksAsync(work.Id, cancellationToken));

            Assert.True(await db.People.AnyAsync(p => p.Id == person.Id, cancellationToken));
            Assert.True(await db.Series.AnyAsync(s => s.Id == series.Id, cancellationToken));
            Assert.True(await db.Tags.AnyAsync(t => t.Id == tag.Id, cancellationToken));
        }
    }

    [Fact]
    public async Task Migrations_match_the_model()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();

        Assert.False(db.Database.HasPendingModelChanges());
    }
}
