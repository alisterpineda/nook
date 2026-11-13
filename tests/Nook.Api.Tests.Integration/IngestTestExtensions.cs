using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features.Jobs;
using Nook.Api.Ingest;

namespace Nook.Api.Tests.Integration;

/// <summary>What tests of ingest share: reads of what an ingest left in the app's database.</summary>
internal static class IngestTestExtensions
{
    /// <summary>The names of an ingest job's steps, in the order they run.</summary>
    public static readonly string[] StepNames =
        ["identify-format", "check-duplicate", "extract-metadata", "extract-cover", "match-work", "file-item"];

    /// <summary>
    /// A tail for names that no other test's people, series or tags can match, since they are shared across
    /// libraries and matched by name.
    /// </summary>
    public static string UniqueSuffix() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>How many works, items and parts the library holds.</summary>
    public static async Task<(int Works, int Items, int Parts)> CountRowsAsync(
        this WebApplicationFactory<Program> app, Library library)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = db.Works.Where(w => w.LibraryId == library.Id);
        var items = db.Items.Where(i => works.Any(w => w.Id == i.WorkId));
        var parts = db.Parts.Where(p => items.Any(i => i.Id == p.ItemId));
        return (
            await works.CountAsync(cancellationToken),
            await items.CountAsync(cancellationToken),
            await parts.CountAsync(cancellationToken));
    }

    /// <summary>The paths of the library's parts, oldest first.</summary>
    public static async Task<List<string>> ReadPartPathsAsync(this WebApplicationFactory<Program> app, Library library)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        return await (
            from part in db.Parts
            join item in db.Items on part.ItemId equals item.Id
            join work in db.Works on item.WorkId equals work.Id
            where work.LibraryId == library.Id
            orderby part.CreatedAt, part.Id
            select part.Path).ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>How many people, series or tags have a name ending in the suffix.</summary>
    public static async Task<int> CountNamedAsync<T>(this WebApplicationFactory<Program> app, string suffix)
        where T : class, INamed
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        return await db.Set<T>().CountAsync(row => row.Name.EndsWith(suffix), TestContext.Current.CancellationToken);
    }

    /// <summary>The result an ingest job ended with.</summary>
    public static IngestResult ReadIngestResult(this JobDetailResponse job)
    {
        Assert.NotNull(job.Result);
        var result = job.Result.Value.Deserialize<IngestResult>(ApiTestExtensions.Json);
        Assert.NotNull(result);
        return result;
    }
}
