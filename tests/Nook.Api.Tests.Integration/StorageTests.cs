using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Storage;

namespace Nook.Api.Tests.Integration;

public class StorageTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Api_refuses_to_start_without_a_data_path()
    {
        await using var app = factory.WithWebHostBuilder(builder => builder.UseSetting("Nook:DataPath", ""));

        var exception = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        var messages = Flatten(exception).Select(inner => inner.Message);
        Assert.Contains(messages, message => message.Contains("Nook:DataPath", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Starting_the_api_creates_the_staging_and_library_folders()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/setup", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        Assert.True(Directory.Exists(Path.Combine(factory.DataPath, "staging")));
        Assert.True(Directory.Exists(Path.Combine(factory.DataPath, "library")));
    }

    [Theory]
    [InlineData("../outside.epub")]
    [InlineData("Books/../../outside.epub")]
    [InlineData("Books/Author/../../../../outside.epub")]
    public void Path_that_leads_outside_the_library_folder_is_refused(string relativePath)
    {
        var storage = factory.Services.GetRequiredService<NookStorage>();
        var library = new Library { Name = "Test", FolderName = "Test" };

        var exception = Assert.Throws<InvalidOperationException>(() => storage.LibraryFile(library, relativePath));

        Assert.Contains("outside the library's folder", exception.Message);
    }

    [Fact]
    public void Path_inside_the_library_folder_is_resolved_under_it()
    {
        var storage = factory.Services.GetRequiredService<NookStorage>();
        var library = new Library { Name = "Test", FolderName = "Test" };

        var path = storage.LibraryFile(library, "Books/Author/Title/Title.epub");

        Assert.Equal(
            Path.GetFullPath(Path.Combine(factory.DataPath, "library", "Test", "Books", "Author", "Title", "Title.epub")),
            path);
    }

    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.SelectMany(Flatten))
                {
                    yield return inner;
                }
            }
        }
    }
}
