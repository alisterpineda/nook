using System.Runtime.Versioning;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nook.Api.Storage;

namespace Nook.Api.Tests.Integration;

public class StorageTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Api_refuses_to_start_without_a_data_path()
    {
        await using var app = factory.WithWebHostBuilder(builder => builder.UseSetting("Nook:DataPath", ""));

        var exception = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        var messages = exception.Flatten().Select(inner => inner.Message);
        Assert.Contains(messages, message => message.Contains("Nook:DataPath", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Api_refuses_to_start_with_the_cache_inside_the_library()
    {
        var library = Path.Combine(factory.DataPath, "books");
        await using var app = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Nook:LibraryPath", library)
            .UseSetting("Nook:CachePath", Path.Combine(library, "cache")));

        var exception = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        var messages = exception.Flatten().Select(inner => inner.Message);
        Assert.Contains(messages, message =>
            message.Contains("Nook:CachePath", StringComparison.Ordinal)
            && message.Contains("Nook:LibraryPath", StringComparison.Ordinal)
            && message.Contains(library, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Api_refuses_to_start_with_the_library_at_the_data_root()
    {
        await using var app = factory.WithWebHostBuilder(builder => builder.UseSetting("Nook:LibraryPath", factory.DataPath));

        var exception = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        var messages = exception.Flatten().Select(inner => inner.Message);
        Assert.Contains(messages, message =>
            message.Contains("Nook:LibraryPath", StringComparison.Ordinal)
            && message.Contains("Nook:DataPath", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Api_with_library_and_cache_paths_creates_those_folders_outside_the_data_root()
    {
        // A data root of its own, so the folders other tests' starts made under the factory's are not counted.
        var elsewhere = Path.Combine(Path.GetTempPath(), "nook-tests", Guid.NewGuid().ToString("N"));
        var dataPath = Path.Combine(elsewhere, "data");
        var library = Path.Combine(elsewhere, "books");
        var cache = Path.Combine(elsewhere, "cache");
        try
        {
            await using var app = factory.WithWebHostBuilder(builder => builder
                .UseSetting("Nook:DataPath", dataPath)
                .UseSetting("Nook:LibraryPath", library)
                .UseSetting("Nook:CachePath", cache));
            using var client = app.CreateClient();
            var response = await client.GetAsync("/api/setup", TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();

            Assert.True(Directory.Exists(library));
            Assert.True(Directory.Exists(Path.Combine(cache, "covers")));
            Assert.True(Directory.Exists(Path.Combine(dataPath, "staging")));
            Assert.True(Directory.Exists(Path.Combine(dataPath, "keys")));
            Assert.False(Directory.Exists(Path.Combine(dataPath, "library")));
            Assert.False(Directory.Exists(Path.Combine(dataPath, "cache")));
        }
        finally
        {
            if (Directory.Exists(elsewhere))
            {
                Directory.Delete(elsewhere, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Starting_the_api_logs_where_each_folder_is()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/setup", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        var logs = factory.ReadLogs(LogLevel.Information);
        var root = Path.GetFullPath(factory.DataPath);
        foreach (var (name, path) in new[]
        {
            ("library", Path.Combine(root, "library")),
            ("cache", Path.Combine(root, "cache")),
            ("staging", Path.Combine(root, "staging")),
            ("keys", Path.Combine(root, "keys")),
        })
        {
            Assert.Single(logs, log => log.Contains($"The {name} folder is {path},", StringComparison.Ordinal));
        }
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task Api_refuses_to_start_when_the_library_folder_is_not_writable()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Folder permissions are set with Unix file modes.");
        }

        Assert.SkipWhen(Environment.UserName == "root", "The root user can write to a read-only folder.");

        var library = Path.Combine(Path.GetTempPath(), "nook-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(library);
        File.SetUnixFileMode(library, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            await using var app = factory.WithWebHostBuilder(builder => builder.UseSetting("Nook:LibraryPath", library));

            var exception = Assert.ThrowsAny<Exception>(() => app.CreateClient());

            var messages = exception.Flatten().Select(inner => inner.Message);
            Assert.Contains(messages, message =>
                message.Contains(library, StringComparison.Ordinal)
                && message.Contains("Nook:LibraryPath", StringComparison.Ordinal)
                && message.Contains("must be able to write", StringComparison.Ordinal));
        }
        finally
        {
            File.SetUnixFileMode(library, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(library, recursive: true);
        }
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task Api_refuses_to_start_when_the_cache_folder_is_not_writable()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Folder permissions are set with Unix file modes.");
        }

        Assert.SkipWhen(Environment.UserName == "root", "The root user can write to a read-only folder.");

        var cache = Path.Combine(Path.GetTempPath(), "nook-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        File.SetUnixFileMode(cache, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            await using var app = factory.WithWebHostBuilder(builder => builder.UseSetting("Nook:CachePath", cache));

            var exception = Assert.ThrowsAny<Exception>(() => app.CreateClient());

            var messages = exception.Flatten().Select(inner => inner.Message);
            Assert.Contains(messages, message =>
                message.Contains($"the cache folder at '{cache}'", StringComparison.Ordinal)
                && message.Contains("Nook:CachePath", StringComparison.Ordinal));
        }
        finally
        {
            File.SetUnixFileMode(cache, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public async Task Starting_the_api_creates_the_staging_library_and_cache_folders()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/setup", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        Assert.True(Directory.Exists(Path.Combine(factory.DataPath, "staging")));
        Assert.True(Directory.Exists(Path.Combine(factory.DataPath, "library")));
        Assert.True(Directory.Exists(factory.CoverCacheFolder()));
    }

    [Theory]
    [InlineData("../outside.epub")]
    [InlineData("Books/../../outside.epub")]
    [InlineData("Books/Author/../../../../outside.epub")]
    public void Path_that_leads_outside_the_library_folder_is_refused(string relativePath)
    {
        var storage = factory.Services.GetRequiredService<NookStorage>();

        var exception = Assert.Throws<InvalidOperationException>(() => storage.LibraryFile(relativePath));

        Assert.Contains("outside the library folder", exception.Message);
    }

    [Fact]
    public void Path_inside_the_library_folder_is_resolved_under_it()
    {
        var storage = factory.Services.GetRequiredService<NookStorage>();

        var path = storage.LibraryFile("Books/Author/Title/Title.epub");

        Assert.Equal(
            Path.GetFullPath(Path.Combine(factory.DataPath, "library", "Books", "Author", "Title", "Title.epub")),
            path);
    }
}
