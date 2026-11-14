using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nook.Api.Data;
using SkiaSharp;

namespace Nook.Api.Tests.Integration;

public class CoverTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Cover_with_no_size_is_the_original_file()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var png = TestImages.Png(80, 120);
        var work = await factory.IngestWithCoverAsync(png);
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/works/{work.Id}/cover", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(png, await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    [Theory]
    [InlineData(UserRole.Member)]
    [InlineData(UserRole.Guest)]
    public async Task Member_and_guest_can_fetch_a_cover(UserRole role)
    {
        var work = await factory.IngestWithCoverAsync(TestImages.Png(80, 120));
        using var client = await factory.CreateSignedInClientAsync(role);

        var response = await client.GetAsync($"/api/works/{work.Id}/cover", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Cover_without_a_session_is_unauthorized()
    {
        var work = await factory.IngestWithCoverAsync(TestImages.Png(80, 120));
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/works/{work.Id}/cover", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cover_of_an_unknown_work_is_not_found()
    {
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/works/{Guid.NewGuid()}/cover", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cover_of_a_work_with_no_cover_is_not_found()
    {
        var work = TestData.NewWork("Parable of the Sower");
        await AddWorkAsync(work);
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/works/{work.Id}/cover", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("cover.jpg", "image/jpeg")]
    [InlineData("cover.jpeg", "image/jpeg")]
    [InlineData("cover.gif", "image/gif")]
    [InlineData("cover.webp", "image/webp")]
    [InlineData("cover.svg", "image/svg+xml")]
    public async Task Cover_is_served_with_the_media_type_of_its_extension(string fileName, string mediaType)
    {
        var work = await factory.IngestWithCoverAsync(TestData.PngCover, fileName, mediaType);
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/works/{work.Id}/cover", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Cover_with_an_extension_no_image_type_stands_for_is_served_as_plain_bytes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = await factory.AddLibraryAsync();
        var folder = factory.LibraryPath(library, "Books");
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "cover.bmp"), TestData.PngCover, cancellationToken);
        var work = TestData.NewWork("Dawn", library.Id);
        work.CoverHash = "0123456789abcdef";
        work.CoverPath = "Books/cover.bmp";
        await AddWorkAsync(work);
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/works/{work.Id}/cover", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TestData.PngCover, await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task Cover_is_served_so_a_browser_runs_nothing_in_it()
    {
        var work = await factory.IngestWithCoverAsync(TestData.PngCover, "cover.svg", "image/svg+xml");
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/works/{work.Id}/cover", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["nosniff"], response.Headers.GetValues("X-Content-Type-Options"));
        Assert.Equal(["default-src 'none'; sandbox"], response.Headers.GetValues("Content-Security-Policy"));
    }

    [Fact]
    public async Task Small_cover_of_a_larger_original_is_a_jpeg_inside_400_by_600()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var work = await factory.IngestWithCoverAsync(TestImages.Png(800, 1200));
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/works/{work.Id}/cover?size=small", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        var image = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        Assert.Equal(SKEncodedImageFormat.Jpeg, TestImages.FormatOf(image));
        Assert.Equal((400, 600), TestImages.SizeOf(image));
    }

    [Fact]
    public async Task Large_cover_of_a_larger_original_is_a_jpeg_inside_1000_by_1500()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var work = await factory.IngestWithCoverAsync(TestImages.Png(2000, 2000));
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/works/{work.Id}/cover?size=large", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        var image = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        Assert.Equal(SKEncodedImageFormat.Jpeg, TestImages.FormatOf(image));
        Assert.Equal((1000, 1000), TestImages.SizeOf(image));
    }

    [Fact]
    public async Task Cover_smaller_than_the_size_keeps_its_dimensions_as_a_jpeg()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var work = await factory.IngestWithCoverAsync(TestImages.Png(100, 150));
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/works/{work.Id}/cover?size=small", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        var image = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        Assert.Equal(SKEncodedImageFormat.Jpeg, TestImages.FormatOf(image));
        Assert.Equal((100, 150), TestImages.SizeOf(image));
    }

    [Theory]
    [InlineData("medium")]
    [InlineData("SMALL")]
    [InlineData("0")]
    [InlineData("400")]
    public async Task Cover_with_an_unknown_size_is_a_validation_error(string size)
    {
        var work = await factory.IngestWithCoverAsync(TestImages.Png(80, 120));
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync(
            $"/api/works/{work.Id}/cover?size={size}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Resized_cover_is_kept_under_its_hash_and_size()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var work = await factory.IngestWithCoverAsync(TestImages.Png(600, 900));
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/works/{work.Id}/cover?size=small", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cached = factory.CoverCachePath(work.CoverHash!, "small");
        Assert.True(File.Exists(cached), $"No cached cover at {cached}.");
        Assert.Equal(
            await response.Content.ReadAsByteArrayAsync(cancellationToken),
            await File.ReadAllBytesAsync(cached, cancellationToken));
    }

    [Fact]
    public async Task Second_request_for_a_size_is_served_from_the_cached_file()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var work = await factory.IngestWithCoverAsync(TestImages.Png(610, 910));
        using var client = await factory.CreateSignedInClientAsync();
        var first = await client.GetAsync($"/api/works/{work.Id}/cover?size=small", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var replacement = TestImages.Png(7, 11);
        await File.WriteAllBytesAsync(factory.CoverCachePath(work.CoverHash!, "small"), replacement, cancellationToken);

        var second = await client.GetAsync($"/api/works/{work.Id}/cover?size=small", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("image/jpeg", second.Content.Headers.ContentType?.MediaType);
        Assert.Equal(replacement, await second.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task Cover_is_resized_again_after_the_cache_is_emptied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var work = await factory.IngestWithCoverAsync(TestImages.Png(620, 920));
        using var client = await factory.CreateSignedInClientAsync();
        var first = await client.GetAsync($"/api/works/{work.Id}/cover?size=small", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var cache = new DirectoryInfo(Path.Combine(factory.DataPath, "cache"));
        foreach (var entry in cache.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo folder)
            {
                folder.Delete(recursive: true);
            }
            else
            {
                entry.Delete();
            }
        }

        var response = await client.GetAsync($"/api/works/{work.Id}/cover?size=small", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        var image = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        Assert.Equal(SKEncodedImageFormat.Jpeg, TestImages.FormatOf(image));
        Assert.True(File.Exists(factory.CoverCachePath(work.CoverHash!, "small")));
    }

    [Fact]
    public async Task Two_works_with_the_same_cover_share_one_cached_file_per_size()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var cover = TestImages.Png(630, 930);
        var first = await factory.IngestWithCoverAsync(cover);
        var second = await factory.IngestWithCoverAsync(cover);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.CoverHash, second.CoverHash);
        using var client = await factory.CreateSignedInClientAsync();

        foreach (var work in new[] { first, second })
        {
            foreach (var size in new[] { "small", "large" })
            {
                var response = await client.GetAsync($"/api/works/{work.Id}/cover?size={size}", cancellationToken);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        var files = Directory.GetFiles(factory.CoverCacheFolder(), $"{first.CoverHash}*")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal);
        Assert.Equal([$"{first.CoverHash}-large.jpg", $"{first.CoverHash}-small.jpg"], files);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("small")]
    [InlineData("large")]
    public async Task Cover_that_is_not_a_readable_image_is_served_as_it_is_at_every_size(string? size)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var work = await factory.IngestWithCoverAsync(TestData.PngCover);
        using var client = await factory.CreateSignedInClientAsync();
        var url = size is null ? $"/api/works/{work.Id}/cover" : $"/api/works/{work.Id}/cover?size={size}";

        var response = await client.GetAsync(url, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TestData.PngCover, await response.Content.ReadAsByteArrayAsync(cancellationToken));
        if (size is not null)
        {
            Assert.Contains(factory.ReadLogs(LogLevel.Warning), m => m.Contains(work.Id.ToString()));
            Assert.Empty(CachedFilesOf(work));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("small")]
    public async Task Cover_whose_file_is_gone_is_not_found_and_logged(string? size)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Dimensions no other test uses, so no cached file for this cover's hash is already there.
        var work = await factory.IngestWithCoverAsync(TestImages.Png(640, 940));
        File.Delete(await CoverFileAsync(work));
        using var client = await factory.CreateSignedInClientAsync();
        var url = size is null ? $"/api/works/{work.Id}/cover" : $"/api/works/{work.Id}/cover?size={size}";

        var response = await client.GetAsync(url, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(factory.ReadLogs(LogLevel.Error), m => m.Contains(work.Id.ToString()));
    }

    /// <summary>Where the work's cover file is on disk.</summary>
    private async Task<string> CoverFileAsync(Work work)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var library = await db.Libraries.SingleAsync(l => l.Id == work.LibraryId, TestContext.Current.CancellationToken);
        return factory.LibraryPath(library, work.CoverPath!.Split('/'));
    }

    [Fact]
    public async Task Cover_too_large_to_decode_is_served_as_it_is_at_a_size()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var cover = TestImages.PngClaiming(16000, 16000);
        var work = await factory.IngestWithCoverAsync(cover);
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/works/{work.Id}/cover?size=small", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(cover, await response.Content.ReadAsByteArrayAsync(cancellationToken));
        Assert.Contains(factory.ReadLogs(LogLevel.Warning), m => m.Contains(work.Id.ToString()));
        Assert.Empty(CachedFilesOf(work));
    }

    [Fact]
    public async Task Resized_cover_is_served_when_it_cannot_be_kept()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var work = await factory.IngestWithCoverAsync(TestImages.Png(650, 950));
        using var client = await factory.CreateSignedInClientAsync();
        var folder = factory.CoverCacheFolder();
        Directory.Delete(folder, recursive: true);
        await File.WriteAllBytesAsync(folder, [], cancellationToken);
        try
        {
            var response = await client.GetAsync($"/api/works/{work.Id}/cover?size=small", cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
            var image = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            Assert.Equal((400, 585), TestImages.SizeOf(image));
            Assert.Contains(factory.ReadLogs(LogLevel.Warning), m => m.Contains(work.CoverHash!));
        }
        finally
        {
            // A file where the folder belongs would fail every later test that keeps a cover.
            File.Delete(folder);
        }
    }

    /// <summary>The files the cache holds for the work's cover, at any size.</summary>
    private string[] CachedFilesOf(Work work)
    {
        // Another test may have emptied the cache, folder and all.
        var folder = factory.CoverCacheFolder();
        return Directory.Exists(folder) ? Directory.GetFiles(folder, $"{work.CoverHash}*") : [];
    }

    private async Task AddWorkAsync(Work work)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        db.Works.Add(work);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
