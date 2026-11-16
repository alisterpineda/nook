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
    /// sections and matched by name.
    /// </summary>
    public static string UniqueSuffix() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>How many works, items and parts the section holds.</summary>
    public static async Task<(int Works, int Items, int Parts)> CountRowsAsync(
        this WebApplicationFactory<Program> app, LibrarySection section)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = db.Works.Where(w => w.SectionId == section.Id);
        var items = db.Items.Where(i => works.Any(w => w.Id == i.WorkId));
        var parts = db.Parts.Where(p => items.Any(i => i.Id == p.ItemId));
        return (
            await works.CountAsync(cancellationToken),
            await items.CountAsync(cancellationToken),
            await parts.CountAsync(cancellationToken));
    }

    /// <summary>The section's parts, oldest first.</summary>
    public static async Task<List<Part>> ReadPartsAsync(this WebApplicationFactory<Program> app, LibrarySection section)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        return await (
            from part in db.Parts
            join item in db.Items on part.ItemId equals item.Id
            join work in db.Works on item.WorkId equals work.Id
            where work.SectionId == section.Id
            orderby part.CreatedAt, part.Id
            select part).AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Uploads the EPUB into the section given, or a section of its own, and returns its one part as the database
    /// holds it.
    /// </summary>
    public static async Task<Part> IngestOnePartAsync(
        this NookApiFactory factory, byte[] epub, LibrarySection? section = null, string fileName = "book.epub")
    {
        section ??= await factory.AddSectionAsync();
        using var admin = await factory.CreateSignedInClientAsync(UserRole.Admin);

        var job = await admin.IngestAsync(epub, fileName, section.Id);
        Assert.Equal(JobStatus.Succeeded, job.Status);

        return Assert.Single(await factory.ReadPartsAsync(section));
    }

    /// <summary>The id of the work the item belongs to.</summary>
    public static async Task<Guid> ReadWorkIdOfItemAsync(this WebApplicationFactory<Program> app, Guid itemId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        return await db.Items.Where(i => i.Id == itemId).Select(i => i.WorkId)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The paths of the section's parts, oldest first.</summary>
    public static async Task<List<string>> ReadPartPathsAsync(this WebApplicationFactory<Program> app, LibrarySection section) =>
        (await app.ReadPartsAsync(section)).Select(part => part.Path).ToList();

    /// <summary>The work row with the id, as the database holds it.</summary>
    public static async Task<Work> ReadWorkAsync(this WebApplicationFactory<Program> app, Guid workId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        return await db.Works.AsNoTracking().SingleAsync(w => w.Id == workId, TestContext.Current.CancellationToken);
    }

    /// <summary>How many people, series or tags have a name ending in the suffix.</summary>
    public static async Task<int> CountNamedAsync<T>(this WebApplicationFactory<Program> app, string suffix)
        where T : NamedEntity
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
