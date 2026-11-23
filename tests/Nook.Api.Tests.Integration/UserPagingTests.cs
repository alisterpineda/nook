using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features;
using Nook.Api.Features.Users;

namespace Nook.Api.Tests.Integration;

/// <summary>A class of its own, so its database holds only the users these tests add.</summary>
public class UserPagingTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Users_come_by_name_in_pages()
    {
        var (abe, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Admin, displayName: "Abe");
        using var _ = client;
        var dee = await factory.CreateUserAsync(displayName: "Dee");
        await Task.Delay(5, TestContext.Current.CancellationToken);
        var lowerDee = await factory.CreateUserAsync(displayName: "dee");
        await Task.Delay(5, TestContext.Current.CancellationToken);
        var carrie = await factory.CreateUserAsync(BuiltInRole.Viewer, displayName: "Carrie");
        await Task.Delay(5, TestContext.Current.CancellationToken);
        var bea = await factory.CreateUserAsync(displayName: "bea");
        await Task.Delay(5, TestContext.Current.CancellationToken);
        var al = await factory.CreateUserAsync(email: "al@example.test");
        // A display name of white space alone counts as none, in the database as in NookUser.ShownName.
        var cy = new NookUser { UserName = "cy", DisplayName = "\t" };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<NookUser>>();
            var result = await users.CreateAsync(cy);
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        var first = await client.GetAsync<Page<UserSummaryResponse>>("/api/users?limit=2");
        var second = await client.GetAsync<Page<UserSummaryResponse>>("/api/users?limit=2&offset=2");
        var unasked = await client.GetAsync<Page<UserSummaryResponse>>("/api/users");

        Assert.Equal([abe.Id, al.Id], first.Items.Select(u => u.Id));
        Assert.Equal((7, 0, 2), (first.Total, first.Offset, first.Limit));
        Assert.Equal([bea.Id, carrie.Id], second.Items.Select(u => u.Id));
        Assert.Equal((7, 2, 2), (second.Total, second.Offset, second.Limit));
        Assert.Equal([abe.Id, al.Id, bea.Id, carrie.Id, cy.Id, dee.Id, lowerDee.Id], unasked.Items.Select(u => u.Id));
        Assert.Equal((7, 0, 50), (unasked.Total, unasked.Offset, unasked.Limit));

        var cyItem = unasked.Items.Single(u => u.Id == cy.Id);
        Assert.Equal("cy", cyItem.Name);
        Assert.Equal("cy", cyItem.Email);

        var carrieItem = unasked.Items.Single(u => u.Id == carrie.Id);
        await using var readScope = factory.Services.CreateAsyncScope();
        var everyone = await readScope.ServiceProvider.GetRequiredService<NookDbContext>().Users
            .AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        var stored = everyone.Single(u => u.Id == carrie.Id);

        // The list shows each user as NookUser does everywhere else.
        Assert.Equal(
            everyone.OrderBy(u => u.Id).Select(u => (u.Id, u.ShownName, u.ShownEmail)),
            unasked.Items.OrderBy(u => u.Id).Select(u => (u.Id, u.Name, u.Email)));
        Assert.Equal(carrie.Email, carrieItem.Email);
        Assert.Equal("Viewer", carrieItem.Role);
        Assert.Equal(stored.CreatedAt, carrieItem.JoinedAt);
        Assert.Equal("Contributor", unasked.Items.Single(u => u.Id == bea.Id).Role);
        Assert.Equal("Admin", unasked.Items.Single(u => u.Id == abe.Id).Role);
    }
}
