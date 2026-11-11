using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Npgsql;

namespace Nook.Api.Tests;

public class IngestStateTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Second_row_for_the_same_item_and_step_is_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var item = await SaveItemAsync("Planet of Exile", cancellationToken);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.ItemSteps.Add(new ItemStep { ItemId = item.Id, StepName = "extract-metadata", StepVersion = 1 });
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.ItemSteps.Add(new ItemStep { ItemId = item.Id, StepName = "extract-metadata", StepVersion = 2 });

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));

            var postgres = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        }
    }

    [Fact]
    public async Task Duplicate_run_can_point_at_an_existing_item()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var item = await SaveItemAsync("City of Illusions", cancellationToken);
        var run = new IngestRun
        {
            LibraryId = Library.DefaultId,
            SourcePath = "incoming/City of Illusions.epub",
            Status = IngestRunStatus.Duplicate,
            ItemId = item.Id,
        };

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.IngestRuns.Add(run);
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var stored = await db.IngestRuns.SingleAsync(r => r.Id == run.Id, cancellationToken);
            Assert.Equal(IngestRunStatus.Duplicate, stored.Status);
            Assert.Equal(item.Id, stored.ItemId);

            var status = await db.Database
                .SqlQuery<string>($"SELECT status AS \"Value\" FROM ingest_runs WHERE id = {run.Id}")
                .SingleAsync(cancellationToken);
            Assert.Equal("Duplicate", status);
        }
    }

    [Fact]
    public async Task Deleting_an_item_clears_the_run_that_produced_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var item = await SaveItemAsync("The Telling", cancellationToken);
        var run = new IngestRun
        {
            LibraryId = Library.DefaultId,
            SourcePath = "incoming/The Telling.epub",
            Status = IngestRunStatus.Succeeded,
            ItemId = item.Id,
        };

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.IngestRuns.Add(run);
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            await db.Items.Where(i => i.Id == item.Id).ExecuteDeleteAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var stored = await db.IngestRuns.SingleAsync(r => r.Id == run.Id, cancellationToken);
            Assert.Null(stored.ItemId);
        }
    }

    private async Task<Item> SaveItemAsync(string title, CancellationToken cancellationToken)
    {
        var work = TestData.NewWork(title);
        var item = new Item { Format = ItemFormat.Epub };
        work.Items.Add(item);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        db.Works.Add(work);
        await db.SaveChangesAsync(cancellationToken);
        return item;
    }
}
