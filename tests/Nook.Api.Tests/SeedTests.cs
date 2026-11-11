using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;

namespace Nook.Api.Tests;

/// <summary>What the migration itself puts in the database. This class inserts nothing.</summary>
public class SeedTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Fresh_database_has_exactly_one_library_named_Library()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();

        var libraries = await db.Libraries.AsNoTracking().ToListAsync(cancellationToken);

        var library = Assert.Single(libraries);
        Assert.Equal("Library", library.Name);
        Assert.Equal("Library", library.FolderName);
        Assert.Equal(7, library.Id.Version);
        Assert.Equal(TimeSpan.Zero, library.CreatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, library.UpdatedAt.Offset);
    }
}
