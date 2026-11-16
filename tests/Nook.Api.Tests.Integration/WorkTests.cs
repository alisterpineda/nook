using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Npgsql;

namespace Nook.Api.Tests.Integration;

public class WorkTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Work_without_a_section_is_saved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var work = new Work { Title = "Unsorted", SortTitle = "Unsorted" };

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.Works.Add(work);
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var saved = await db.Works.AsNoTracking().SingleAsync(w => w.Id == work.Id, cancellationToken);

            Assert.Null(saved.SectionId);
        }
    }

    [Fact]
    public async Task Work_naming_an_unknown_section_is_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        db.Works.Add(new Work { SectionId = Guid.NewGuid(), Title = "Orphan", SortTitle = "Orphan" });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));

        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgres.SqlState);
    }

    [Fact]
    public async Task Work_holds_one_series_with_a_decimal_position()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var series = new Series { Name = "The Expanse" };
        var work = new Work
        {
            Title = "The Churn",
            SortTitle = "Churn, The",
            SeriesId = series.Id,
            SeriesPosition = 1.5m,
        };

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.Series.Add(series);
            db.Works.Add(work);
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var saved = await db.Works.AsNoTracking().SingleAsync(w => w.Id == work.Id, cancellationToken);

            Assert.Equal(series.Id, saved.SeriesId);
            Assert.Equal(1.5m, saved.SeriesPosition);
        }

        Assert.Equal("numeric", await ReadColumnTypeAsync("series_position", cancellationToken));
    }

    [Fact]
    public async Task Works_have_a_gin_index_on_the_search_vector()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();

        var definitions = await db.ReadIndexDefinitionsAsync("works", cancellationToken);

        Assert.Contains(definitions, d => d.Contains("USING gin (search_vector)"));
        Assert.Equal("tsvector", await ReadColumnTypeAsync("search_vector", cancellationToken));
    }

    [Fact]
    public async Task Section_with_works_cannot_be_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var section = new LibrarySection { Name = "Comics" };

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.Sections.Add(section);
            db.Works.Add(new Work { SectionId = section.Id, Title = "Saga", SortTitle = "Saga" });
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var tracked = await db.Sections.SingleAsync(s => s.Id == section.Id, cancellationToken);
            db.Sections.Remove(tracked);

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));

            var postgres = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.RestrictViolation, postgres.SqlState);
        }
    }

    [Fact]
    public async Task Series_with_works_cannot_be_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var series = new Series { Name = "Discworld" };

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.Series.Add(series);
            db.Works.Add(new Work
            {
                Title = "Mort",
                SortTitle = "Mort",
                SeriesId = series.Id,
                SeriesPosition = 4m,
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var tracked = await db.Series.SingleAsync(s => s.Id == series.Id, cancellationToken);
            db.Series.Remove(tracked);

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));

            var postgres = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.RestrictViolation, postgres.SqlState);
        }
    }

    private async Task<string?> ReadColumnTypeAsync(string column, CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        return await db.Database
            .SqlQuery<string>($"SELECT udt_name::text AS \"Value\" FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'works' AND column_name = {column}")
            .SingleOrDefaultAsync(cancellationToken);
    }
}
