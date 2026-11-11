using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;

namespace Nook.Api.Tests.Integration;

public class LibraryTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task New_library_gets_a_version_7_id_and_timestamps()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var before = DateTimeOffset.UtcNow;
        var library = new Library { Name = "Comics", FolderName = "Comics" };

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.Libraries.Add(library);
            await db.SaveChangesAsync(cancellationToken);
        }

        var created = await FindAsync(library.Id, cancellationToken);

        Assert.Equal(7, created.Id.Version);
        Assert.Equal(TimeSpan.Zero, created.CreatedAt.Offset);
        Assert.InRange(created.CreatedAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(created.CreatedAt, created.UpdatedAt);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var tracked = await db.Libraries.SingleAsync(l => l.Id == library.Id, cancellationToken);
            tracked.Name = "Graphic novels";
            await db.SaveChangesAsync(cancellationToken);
        }

        var changed = await FindAsync(library.Id, cancellationToken);

        Assert.Equal("Graphic novels", changed.Name);
        Assert.Equal(created.CreatedAt, changed.CreatedAt);
        Assert.True(changed.UpdatedAt > created.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, changed.UpdatedAt.Offset);
    }

    private async Task<Library> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        return await db.Libraries.AsNoTracking().SingleAsync(l => l.Id == id, cancellationToken);
    }
}
