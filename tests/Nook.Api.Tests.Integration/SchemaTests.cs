using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;

namespace Nook.Api.Tests.Integration;

/// <summary>Pins the whole schema, the cascade from a work and the cascade from a job.</summary>
public class SchemaTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    private static readonly string[] IdentityChildTables =
        ["asp_net_user_claims", "asp_net_user_logins", "asp_net_user_tokens"];

    private static readonly string[] NookTables =
    [
        "asp_net_users", "roles", "role_sections", "settings", "sections", "works", "items", "parts", "people", "work_contributors", "series", "tags",
        "work_external_ids", "jobs", "job_steps",
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
            t.Contains("grant") || t.Contains("progress") || t.Contains("state") || t.Contains("shel"));
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
            Assert.False(await db.WorkContributors.AnyAsync(c => c.WorkId == work.Id, cancellationToken));
            Assert.False(await db.WorkExternalIds.AnyAsync(e => e.WorkId == work.Id, cancellationToken));
            Assert.Equal(0, await db.CountTagLinksAsync(work.Id, cancellationToken));

            Assert.True(await db.People.AnyAsync(p => p.Id == person.Id, cancellationToken));
            Assert.True(await db.Series.AnyAsync(s => s.Id == series.Id, cancellationToken));
            Assert.True(await db.Tags.AnyAsync(t => t.Id == tag.Id, cancellationToken));
        }
    }

    [Fact]
    public async Task Job_tables_hold_only_generic_columns()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();

        var columns = await db.Database
            .SqlQuery<string>($"SELECT (table_name || '.' || column_name)::text AS \"Value\" FROM information_schema.columns WHERE table_schema = 'public' AND table_name IN ('jobs', 'job_steps')")
            .ToListAsync(cancellationToken);

        string[] expected =
        [
            "jobs.id", "jobs.created_at", "jobs.updated_at", "jobs.type", "jobs.status", "jobs.payload", "jobs.result",
            "jobs.parent_job_id", "jobs.not_before", "jobs.attempts_made", "jobs.attempts_allowed", "jobs.reclaims_made",
            "jobs.lease_expires_at", "jobs.error",
            "job_steps.id", "job_steps.created_at", "job_steps.updated_at", "job_steps.job_id", "job_steps.name",
            "job_steps.order", "job_steps.attempt", "job_steps.status", "job_steps.error", "job_steps.started_at",
            "job_steps.finished_at",
        ];
        Assert.Equal(expected.Order(), columns.Order());
    }

    [Fact]
    public async Task Deleting_a_job_removes_its_steps()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = new Job { Type = "test", Payload = "{}" };
        var startedAt = DateTimeOffset.UtcNow;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.Jobs.Add(job);
            db.JobSteps.AddRange(
                new JobStep { JobId = job.Id, Name = "first", Order = 1, Attempt = 1, StartedAt = startedAt },
                new JobStep { JobId = job.Id, Name = "second", Order = 2, Attempt = 1, StartedAt = startedAt });
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            Assert.Equal(2, await db.JobSteps.CountAsync(s => s.JobId == job.Id, cancellationToken));

            // Deleted without loading its steps, so the database's own delete rule does the work.
            var tracked = await db.Jobs.SingleAsync(j => j.Id == job.Id, cancellationToken);
            db.Jobs.Remove(tracked);
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();

            Assert.False(await db.Jobs.AnyAsync(j => j.Id == job.Id, cancellationToken));
            Assert.False(await db.JobSteps.AnyAsync(s => s.JobId == job.Id, cancellationToken));
        }
    }

    [Theory]
    [InlineData("people")]
    [InlineData("series")]
    [InlineData("tags")]
    public async Task Named_tables_hold_an_indexed_name_key(string table)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();

        var column = await db.Database
            .SqlQuery<string>($"SELECT (column_name || ' ' || udt_name || ' ' || is_nullable)::text AS \"Value\" FROM information_schema.columns WHERE table_schema = 'public' AND table_name = {table} AND column_name = 'name_key'")
            .SingleAsync(cancellationToken);
        var indexes = await db.ReadIndexDefinitionsAsync(table, cancellationToken);

        Assert.Equal("name_key text NO", column);
        Assert.Contains(indexes, i => i.EndsWith("USING btree (name_key)") && !i.Contains("UNIQUE"));
    }

    [Fact]
    public async Task Changing_a_name_changes_its_key()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tag = new Tag { Name = "  Science   Fiction " };

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.Tags.Add(tag);
            await db.SaveChangesAsync(cancellationToken);
            Assert.Equal("science fiction", await ReadNameKeyAsync(db, tag.Id, cancellationToken));
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var tracked = await db.Tags.SingleAsync(t => t.Id == tag.Id, cancellationToken);
            tracked.Name = "Space Opera";
            await db.SaveChangesAsync(cancellationToken);
            Assert.Equal("space opera", await ReadNameKeyAsync(db, tag.Id, cancellationToken));
        }
    }

    [Fact]
    public async Task Migrations_match_the_model()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Search_configuration_keeps_every_word_and_drops_case_and_accents()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();

        var vector = await db.Database
            .SqlQuery<string>($"SELECT to_tsvector('nook', 'The Brontë It')::text AS \"Value\"")
            .SingleAsync(cancellationToken);

        Assert.Contains("'the'", vector);
        Assert.Contains("'bronte'", vector);
        Assert.Contains("'it'", vector);
        Assert.DoesNotContain("brontë", vector);
    }

    /// <summary>The tag's name key as the database holds it.</summary>
    private static Task<string> ReadNameKeyAsync(NookDbContext db, Guid tagId, CancellationToken cancellationToken) =>
        db.Database
            .SqlQuery<string>($"SELECT name_key AS \"Value\" FROM tags WHERE id = {tagId}")
            .SingleAsync(cancellationToken);
}
