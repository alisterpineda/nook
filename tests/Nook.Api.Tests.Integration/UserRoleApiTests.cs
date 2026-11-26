using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features;
using Nook.Api.Features.Audit;
using Nook.Api.Features.Auth;
using Nook.Api.Features.Users;

namespace Nook.Api.Tests.Integration;

public class UserRoleApiTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Changed_role_shows_in_the_list_and_reaches_the_users_session()
    {
        await using var app = factory.WithSessionsCheckedOnEveryRequest();
        var admin = await app.CreateUserAsync(BuiltInRole.Admin);
        var user = await app.CreateUserAsync(BuiltInRole.Viewer);
        using var adminClient = app.CreateClient();
        using var userClient = app.CreateClient();
        (await adminClient.LoginAsync(admin.Email!, ApiTestExtensions.Password)).EnsureSuccessStatusCode();
        (await userClient.LoginAsync(user.Email!, ApiTestExtensions.Password)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync("/api/users", TestContext.Current.CancellationToken)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await PutAsync(adminClient, user.Id, "admin")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync("/api/users", TestContext.Current.CancellationToken)).StatusCode);
        var listed = await adminClient.GetAsync<Page<UserSummaryResponse>>("/api/users?limit=200");
        Assert.Equal("Admin", listed.Items.Single(u => u.Id == user.Id).Role);

        Assert.Equal(HttpStatusCode.NoContent, (await PutAsync(adminClient, user.Id, "viewer")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync("/api/users", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task User_on_a_role_that_is_not_built_in_can_be_moved_to_a_built_in_one()
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var role = ApiTestExtensions.NewRole("Custom", canUpload: false);
        await factory.AddRowsAsync(role);
        var user = await factory.CreateUserAsync(role.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await PutAsync(admin, user.Id, "contributor")).StatusCode);

        Assert.Equal(Role.ContributorId, await RoleIdOfAsync(factory, user.Id));
    }

    [Fact]
    public async Task Anonymous_role_is_refused()
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var user = await factory.CreateUserAsync(BuiltInRole.Viewer);

        var response = await PutAsync(admin, user.Id, "anonymous");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Role", await response.ErrorFieldsAsync());
        Assert.Equal(Role.ViewerId, await RoleIdOfAsync(factory, user.Id));
    }

    [Fact]
    public async Task Request_with_no_role_is_refused()
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var user = await factory.CreateUserAsync(BuiltInRole.Viewer);

        var response = await admin.PutAsJsonAsync($"/api/users/{user.Id}/role", new { }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Role", await response.ErrorFieldsAsync());
        Assert.Equal(Role.ViewerId, await RoleIdOfAsync(factory, user.Id));
    }

    [Fact]
    public async Task Unknown_user_is_not_found()
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await PutAsync(admin, Guid.NewGuid(), "viewer");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_change_their_own_role()
    {
        var (admin, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Admin);
        using var _ = client;
        await factory.CreateUserAsync(BuiltInRole.Admin);

        var response = await PutAsync(client, admin.Id, "viewer");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(BuiltInRole.Admin, (await client.GetAsync<CurrentUserResponse>("/api/auth/me")).Role);
        Assert.Equal(Role.AdminId, await RoleIdOfAsync(factory, admin.Id));
    }

    [Theory]
    [InlineData(BuiltInRole.Contributor)]
    [InlineData(BuiltInRole.Viewer)]
    public async Task Contributor_or_viewer_is_forbidden_from_changing_a_role(BuiltInRole role)
    {
        using var client = await factory.CreateSignedInClientAsync(role);
        var user = await factory.CreateUserAsync(BuiltInRole.Viewer);

        var response = await PutAsync(client, user.Id, "admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(Role.ViewerId, await RoleIdOfAsync(factory, user.Id));
    }

    [Fact]
    public async Task Signed_out_caller_cannot_change_a_role()
    {
        using var client = factory.CreateClient();
        var user = await factory.CreateUserAsync(BuiltInRole.Viewer);

        var response = await PutAsync(client, user.Id, "admin");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(Role.ViewerId, await RoleIdOfAsync(factory, user.Id));
    }

    [Fact]
    public async Task Role_single_sign_on_sets_cannot_be_changed()
    {
        var withLogin = await factory.CreateUserAsync(BuiltInRole.Viewer);
        var passwordOnly = await factory.CreateUserAsync(BuiltInRole.Viewer);
        await factory.AddSingleSignOnLoginAsync(withLogin.Id);

        await using var app = factory.WithSingleSignOn().WithWebHostBuilder(b => b.UseSetting("Nook:Oidc:AdminGroup", "nook-admins"));
        using var admin = await app.CreateSignedInClientAsync(BuiltInRole.Admin);

        var refused = await PutAsync(admin, withLogin.Id, "contributor");
        var allowed = await PutAsync(admin, passwordOnly.Id, "contributor");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(Role.ViewerId, await RoleIdOfAsync(factory, withLogin.Id));
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        Assert.Equal(Role.ContributorId, await RoleIdOfAsync(factory, passwordOnly.Id));
    }

    [Fact]
    public async Task Changing_a_role_is_recorded_with_the_role_before_and_after()
    {
        var (admin, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Admin);
        using var _ = client;
        var user = await factory.CreateUserAsync(BuiltInRole.Viewer);

        (await PutAsync(client, user.Id, "contributor")).EnsureSuccessStatusCode();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var entry = await db.AuditEntries.SingleAsync(e => e.Action == AuditActions.UserRoleChanged && e.SubjectId == user.Id, TestContext.Current.CancellationToken);
        Assert.Equal(admin.Id, entry.ActorId);
        Assert.Equal(AuditSubjectTypes.User, entry.SubjectType);
        using var details = JsonDocument.Parse(entry.Details);
        Assert.Equal("Viewer", details.RootElement.GetProperty("before").GetString());
        Assert.Equal("Contributor", details.RootElement.GetProperty("after").GetString());
    }

    [Fact]
    public async Task Role_the_user_already_holds_is_not_recorded_as_a_change()
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var user = await factory.CreateUserAsync(BuiltInRole.Viewer);

        var response = await PutAsync(admin, user.Id, "viewer");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        Assert.False(await db.AuditEntries.AnyAsync(e => e.Action == AuditActions.UserRoleChanged && e.SubjectId == user.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Number_that_names_no_role_is_refused()
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var user = await factory.CreateUserAsync(BuiltInRole.Viewer);

        var response = await admin.PutAsJsonAsync($"/api/users/{user.Id}/role", new { role = 7 }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Role", await response.ErrorFieldsAsync());
        Assert.Equal(Role.ViewerId, await RoleIdOfAsync(factory, user.Id));
    }

    internal static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, string role) =>
        client.PutAsJsonAsync($"/api/users/{id}/role", new { role }, TestContext.Current.CancellationToken);

    internal static async Task<Guid> RoleIdOfAsync(NookApiFactory app, Guid userId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        return await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.RoleId).SingleAsync(TestContext.Current.CancellationToken);
    }
}

public class LastAdminRoleTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task The_only_admin_left_cannot_be_demoted_by_a_session_that_was_an_admin()
    {
        var (a, clientA) = await factory.CreateSignedInUserAsync(BuiltInRole.Admin);
        var (b, clientB) = await factory.CreateSignedInUserAsync(BuiltInRole.Admin);
        using var _a = clientA;
        using var _b = clientB;

        Assert.Equal(HttpStatusCode.NoContent, (await UserRoleApiTests.PutAsync(clientA, b.Id, "viewer")).StatusCode);

        // B's cookie still says admin, though B is a viewer now.
        var response = await UserRoleApiTests.PutAsync(clientB, a.Id, "viewer");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(Role.AdminId, await UserRoleApiTests.RoleIdOfAsync(factory, a.Id));
    }
}

public class LastAdminRoleRaceTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Admins_demoting_each_other_at_once_leave_an_admin()
    {
        var responses = await factory.AdminsActOnEachOtherAtOnceAsync(
            (client, next) => UserRoleApiTests.PutAsync(client, next, "viewer"));

        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(u => u.RoleId == Role.AdminId, TestContext.Current.CancellationToken));
    }
}
