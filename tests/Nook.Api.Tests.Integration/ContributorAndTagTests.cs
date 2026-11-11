using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Npgsql;

namespace Nook.Api.Tests.Integration;

public class ContributorAndTagTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Contributor_role_defaults_to_author()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var person = new Person { Name = "Ursula K. Le Guin", SortName = "Le Guin, Ursula K." };
        var work = TestData.NewWork("A Wizard of Earthsea");
        var contributor = new WorkContributor { WorkId = work.Id, PersonId = person.Id };

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.People.Add(person);
            db.Works.Add(work);
            db.WorkContributors.Add(contributor);
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var saved = await db.WorkContributors.AsNoTracking().SingleAsync(c => c.Id == contributor.Id, cancellationToken);
            Assert.Equal(ContributorRole.Author, saved.Role);

            var stored = await db.Database
                .SqlQuery<string>($"SELECT role AS \"Value\" FROM work_contributors WHERE id = {contributor.Id}")
                .SingleAsync(cancellationToken);
            Assert.Equal("Author", stored);
        }
    }

    [Fact]
    public async Task Contributor_display_order_is_stored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var author = new Person { Name = "Neil Gaiman", SortName = "Gaiman, Neil" };
        var coAuthor = new Person { Name = "Terry Pratchett", SortName = "Pratchett, Terry" };
        var work = TestData.NewWork("Good Omens");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.People.AddRange(author, coAuthor);
            db.Works.Add(work);
            // Saved out of display order, so reading back in order proves the stored order is used.
            db.WorkContributors.Add(new WorkContributor { WorkId = work.Id, PersonId = coAuthor.Id, Order = 1 });
            db.WorkContributors.Add(new WorkContributor { WorkId = work.Id, PersonId = author.Id, Order = 0 });
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var people = await db.WorkContributors
                .Where(c => c.WorkId == work.Id)
                .OrderBy(c => c.Order)
                .Select(c => c.PersonId)
                .ToListAsync(cancellationToken);

            Assert.Equal([author.Id, coAuthor.Id], people);
        }
    }

    [Fact]
    public async Task One_tag_can_be_on_two_works()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tag = new Tag { Name = "Fantasy" };
        var first = TestData.NewWork("The Tombs of Atuan");
        var second = TestData.NewWork("The Farthest Shore");
        first.Tags.Add(tag);
        second.Tags.Add(tag);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.Works.AddRange(first, second);
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var tagged = await db.Works
                .Where(w => w.Tags.Any(t => t.Id == tag.Id))
                .Select(w => w.Id)
                .ToListAsync(cancellationToken);

            Assert.Equivalent(new[] { first.Id, second.Id }, tagged);
            Assert.Equal(1, await db.Tags.CountAsync(t => t.Id == tag.Id, cancellationToken));
        }
    }

    [Fact]
    public async Task Deleting_a_tag_removes_its_links()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tag = new Tag { Name = "Unread" };
        var work = TestData.NewWork("Tehanu");
        work.Tags.Add(tag);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.Works.Add(work);
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            await db.Tags.Where(t => t.Id == tag.Id).ExecuteDeleteAsync(cancellationToken);

            var links = await db.CountTagLinksAsync(work.Id, cancellationToken);
            Assert.Equal(0, links);
            Assert.True(await db.Works.AnyAsync(w => w.Id == work.Id, cancellationToken));
        }
    }

    [Fact]
    public async Task Person_with_contributions_cannot_be_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var person = new Person { Name = "Octavia E. Butler", SortName = "Butler, Octavia E." };
        var work = TestData.NewWork("Kindred");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            db.People.Add(person);
            db.Works.Add(work);
            db.WorkContributors.Add(new WorkContributor { WorkId = work.Id, PersonId = person.Id });
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var tracked = await db.People.SingleAsync(p => p.Id == person.Id, cancellationToken);
            db.People.Remove(tracked);

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));

            var postgres = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.RestrictViolation, postgres.SqlState);
        }
    }
}
