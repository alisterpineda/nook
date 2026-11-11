using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Npgsql;

namespace Nook.Api.Tests;

public class WorkExternalIdTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Work_can_hold_two_values_for_one_scheme()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var work = TestData.NewWork("The Left Hand of Darkness");
        work.ExternalIds.Add(new WorkExternalId { Scheme = "isbn", Value = "9780441478125" });
        work.ExternalIds.Add(new WorkExternalId { Scheme = "isbn", Value = "9780060125745" });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.Works.Add(work);
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var values = await db.WorkExternalIds
                .Where(e => e.WorkId == work.Id && e.Scheme == "isbn")
                .Select(e => e.Value)
                .ToListAsync(cancellationToken);

            Assert.Equivalent(new[] { "9780441478125", "9780060125745" }, values);
        }
    }

    [Fact]
    public async Task Same_work_scheme_and_value_twice_is_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var work = TestData.NewWork("The Dispossessed");
        work.ExternalIds.Add(new WorkExternalId { Scheme = "isbn", Value = "9780060512750" });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.Works.Add(work);
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.WorkExternalIds.Add(new WorkExternalId { WorkId = work.Id, Scheme = "isbn", Value = "9780060512750" });

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));

            var postgres = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        }
    }

    [Fact]
    public async Task Two_works_can_share_a_scheme_and_value()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = TestData.NewWork("The Lathe of Heaven");
        var second = TestData.NewWork("The Lathe of Heaven (Omnibus)");
        first.ExternalIds.Add(new WorkExternalId { Scheme = "isbn", Value = "9781416556961" });
        second.ExternalIds.Add(new WorkExternalId { Scheme = "isbn", Value = "9781416556961" });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.Works.AddRange(first, second);
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var works = await db.WorkExternalIds
                .Where(e => e.Scheme == "isbn" && e.Value == "9781416556961")
                .Select(e => e.WorkId)
                .ToListAsync(cancellationToken);

            Assert.Equivalent(new[] { first.Id, second.Id }, works);
        }
    }

    [Fact]
    public async Task External_ids_are_indexed_on_scheme_and_value()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();

        var definitions = await db.ReadIndexDefinitionsAsync("work_external_ids", cancellationToken);

        Assert.Contains(definitions, d => d.EndsWith("USING btree (scheme, value)"));
        Assert.Contains(definitions, d => d.StartsWith("CREATE UNIQUE INDEX") && d.EndsWith("USING btree (work_id, scheme, value)"));
    }
}
