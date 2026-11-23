using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features;
using Nook.Api.Features.Users;

namespace Nook.Api.Tests.Integration;

public class UserListApiTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Theory]
    [InlineData(BuiltInRole.Contributor)]
    [InlineData(BuiltInRole.Viewer)]
    public async Task Contributor_or_viewer_is_forbidden_from_listing_users(BuiltInRole role)
    {
        using var client = await factory.CreateSignedInClientAsync(role);

        var response = await client.GetAsync("/api/users", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Signed_out_caller_is_not_let_in_to_list_users()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/users", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Users_can_be_sorted_by_email_in_either_direction()
    {
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var zed = await factory.CreateUserAsync(email: $"Zed-{Guid.NewGuid():N}@example.test");
        var amy = await factory.CreateUserAsync(email: $"amy-{Guid.NewGuid():N}@example.test");
        var moe = await factory.CreateUserAsync(email: $"Moe-{Guid.NewGuid():N}@example.test");

        var ascending = await IdsAsync(client, "sort=email", zed, amy, moe);
        var descending = await IdsAsync(client, "sort=email&direction=desc", zed, amy, moe);

        Assert.Equal([amy.Id, moe.Id, zed.Id], ascending);
        Assert.Equal([zed.Id, moe.Id, amy.Id], descending);
    }

    [Fact]
    public async Task Users_can_be_sorted_by_role_name_including_a_role_that_is_not_built_in()
    {
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var librarian = new Role { Name = "Librarian", ContentAccess = ContentAccess.Browse };
        await factory.AddRowsAsync(librarian);
        var viewer = await factory.CreateUserAsync(BuiltInRole.Viewer);
        var admin = await factory.CreateUserAsync(BuiltInRole.Admin);
        var librarianUser = await factory.CreateUserAsync(librarian.Id);
        var contributor = await factory.CreateUserAsync(BuiltInRole.Contributor);

        var ids = await IdsAsync(client, "sort=role", viewer, admin, librarianUser, contributor);

        Assert.Equal([admin.Id, contributor.Id, librarianUser.Id, viewer.Id], ids);
    }

    [Fact]
    public async Task Users_can_be_sorted_by_when_they_joined()
    {
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var first = await factory.CreateUserAsync(displayName: "Zoe");
        await Task.Delay(5, TestContext.Current.CancellationToken);
        var second = await factory.CreateUserAsync(displayName: "Yan");
        await Task.Delay(5, TestContext.Current.CancellationToken);
        var third = await factory.CreateUserAsync(displayName: "Xia");

        var ascending = await IdsAsync(client, "sort=joined", third, first, second);
        var descending = await IdsAsync(client, "sort=joined&direction=desc", third, first, second);

        Assert.Equal([first.Id, second.Id, third.Id], ascending);
        Assert.Equal([third.Id, second.Id, first.Id], descending);
    }

    [Fact]
    public async Task Users_with_the_same_sort_value_come_in_id_order()
    {
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var first = await factory.CreateUserAsync(BuiltInRole.Viewer, displayName: "Zoe");
        await Task.Delay(5, TestContext.Current.CancellationToken);
        var second = await factory.CreateUserAsync(BuiltInRole.Viewer, displayName: "Yan");
        await Task.Delay(5, TestContext.Current.CancellationToken);
        var third = await factory.CreateUserAsync(BuiltInRole.Viewer, displayName: "Xia");

        var ascending = await IdsAsync(client, "sort=role", third, first, second);
        var descending = await IdsAsync(client, "sort=role&direction=desc", third, first, second);

        Assert.Equal([first.Id, second.Id, third.Id], ascending);
        Assert.Equal([first.Id, second.Id, third.Id], descending);
    }

    [Theory]
    [InlineData("sort=shoe-size")]
    [InlineData("direction=sideways")]
    public async Task An_unknown_sort_or_direction_is_a_bad_request(string query)
    {
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await client.GetAsync($"/api/users?{query}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_user_says_whether_a_password_is_stored_and_a_login_is_linked()
    {
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var passwordOnly = await factory.CreateUserAsync();
        var loginOnly = new NookUser { UserName = Guid.NewGuid().ToString("N"), RoleId = Role.IdOf(BuiltInRole.Contributor) };
        var both = await factory.CreateUserAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<NookUser>>();
            Assert.True((await users.CreateAsync(loginOnly)).Succeeded);
            Assert.True((await users.AddLoginAsync(loginOnly, new UserLoginInfo("pocket-id", Guid.NewGuid().ToString("N"), "Pocket ID"))).Succeeded);
            var tracked = await users.FindByIdAsync(both.Id.ToString());
            Assert.True((await users.AddLoginAsync(tracked!, new UserLoginInfo("pocket-id", Guid.NewGuid().ToString("N"), "Pocket ID"))).Succeeded);
        }

        var page = await client.GetAsync<Page<UserSummaryResponse>>("/api/users?limit=200");

        Assert.Equal((true, false), SignIn(page, passwordOnly.Id));
        Assert.Equal((false, true), SignIn(page, loginOnly.Id));
        Assert.Equal((true, true), SignIn(page, both.Id));
    }

    [Fact]
    public async Task A_stored_password_is_still_reported_while_password_sign_in_is_off()
    {
        var admin = await factory.CreateUserAsync(BuiltInRole.Admin);
        using var on = factory.CreateClient();
        var login = await on.LoginAsync(admin.Email!, ApiTestExtensions.Password);
        login.EnsureSuccessStatusCode();
        var sessionCookie = login.SessionCookiePair();

        await using var app = factory.WithSingleSignOn(passwordSignIn: false);
        using var off = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users?limit=200");
        request.Headers.Add("Cookie", sessionCookie);
        var response = await off.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.ReadAsync<Page<UserSummaryResponse>>();
        Assert.True(page.Items.Single(u => u.Id == admin.Id).HasPassword);
    }

    [Fact]
    public async Task A_user_says_whether_the_audit_log_names_them()
    {
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var named = await factory.CreateUserAsync();
        var unnamed = await factory.CreateUserAsync();
        await factory.AddRowsAsync(new AuditEntry
        {
            ActorId = named.Id,
            ActorName = "Someone",
            ActorEmail = named.Email!,
            Action = "upload.submitted",
            SubjectType = "work",
            SubjectId = Guid.NewGuid(),
            SubjectName = "A subject",
            Details = "{}",
        });

        var page = await client.GetAsync<Page<UserSummaryResponse>>("/api/users?limit=200");

        Assert.True(page.Items.Single(u => u.Id == named.Id).HasAuditEntries);
        Assert.False(page.Items.Single(u => u.Id == unnamed.Id).HasAuditEntries);
    }

    private static (bool HasPassword, bool HasSingleSignOn) SignIn(Page<UserSummaryResponse> page, Guid id)
    {
        var user = page.Items.Single(u => u.Id == id);
        return (user.HasPassword, user.HasSingleSignOn);
    }

    /// <summary>The ids of the given users, in the order the list puts them.</summary>
    private static async Task<Guid[]> IdsAsync(HttpClient client, string query, params NookUser[] users)
    {
        var page = await client.GetAsync<Page<UserSummaryResponse>>($"/api/users?limit=200&{query}");
        var wanted = users.Select(u => u.Id).ToHashSet();
        return page.Items.Select(u => u.Id).Where(wanted.Contains).ToArray();
    }
}
