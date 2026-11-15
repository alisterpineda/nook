using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;

namespace Nook.Api.Tests.Integration;

/// <summary>What tests of covers share: works that have one.</summary>
internal static class CoverTestExtensions
{
    /// <summary>
    /// Uploads an EPUB whose cover holds the bytes given, into a library of its own, and returns its work as the
    /// database holds it.
    /// </summary>
    public static async Task<Work> IngestWithCoverAsync(
        this NookApiFactory factory, byte[] cover, string fileName = "cover.png", string mediaType = "image/png")
    {
        var library = await factory.AddLibraryAsync();
        var epub = new EpubBuilder().WithTitle("Kindred").WithCreator("Octavia E. Butler")
            .WithCover(cover, fileName, mediaType)
            .Build();
        using var admin = await factory.CreateSignedInClientAsync(UserRole.Admin);

        var job = await admin.IngestAsync(epub, "kindred.epub", library.Id);
        Assert.Equal(JobStatus.Succeeded, job.Status);

        var itemId = job.ReadIngestResult().ItemId;
        return await factory.ReadWorkAsync(await factory.ReadWorkIdOfItemAsync(itemId));
    }

    /// <summary>The path a cover resized to a size is kept at under the app's data root.</summary>
    public static string CoverCachePath(this NookApiFactory factory, string coverHash, string size) =>
        Path.Combine(factory.CoverCacheFolder(), $"{coverHash}-{size}.jpg");

    /// <summary>The folder resized covers are kept in.</summary>
    public static string CoverCacheFolder(this NookApiFactory factory) =>
        Path.Combine(factory.DataPath, "cache", "covers");
}
