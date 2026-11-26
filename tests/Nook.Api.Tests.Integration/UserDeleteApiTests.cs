using System.Net;
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

public class UserDeleteApiTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Deleted_user_cannot_sign_in_and_is_gone_from_the_list()
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var user = await factory.CreateUserAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(admin, user.Id)).StatusCode);

        using var other = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.LoginAsync(user.Email!, ApiTestExtensions.Password)).StatusCode);
        var listed = await admin.GetAsync<Page<UserSummaryResponse>>("/api/users?limit=200");
        Assert.DoesNotContain(listed.Items, u => u.Id == user.Id);
    }

    [Fact]
    public async Task Deleting_a_user_ends_their_sessions()
    {
        await using var app = factory.WithSessionsCheckedOnEveryRequest();
        var admin = await app.CreateUserAsync(BuiltInRole.Admin);
        var user = await app.CreateUserAsync();
        using var adminClient = app.CreateClient();
        using var userClient = app.CreateClient();
        (await adminClient.LoginAsync(admin.Email!, ApiTestExtensions.Password)).EnsureSuccessStatusCode();
        (await userClient.LoginAsync(user.Email!, ApiTestExtensions.Password)).EnsureSuccessStatusCode();

        (await DeleteAsync(adminClient, user.Id)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await userClient.GetAsync("/api/auth/me", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync("/api/auth/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Books_a_deleted_user_added_stay_with_no_owner()
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var user = await factory.CreateUserAsync();
        var work = TestData.NewWork("Left behind");
        work.OwnerId = user.Id;
        await factory.AddRowsAsync(work);

        (await DeleteAsync(admin, user.Id)).EnsureSuccessStatusCode();

        Assert.Null((await factory.ReadWorkAsync(work.Id)).OwnerId);
    }

    [Fact]
    public async Task Deleting_a_user_with_a_single_sign_on_login_removes_the_login()
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var user = await factory.CreateUserAsync();
        await factory.AddSingleSignOnLoginAsync(user.Id);

        (await DeleteAsync(admin, user.Id)).EnsureSuccessStatusCode();

        await using var check = factory.Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<NookDbContext>();
        Assert.False(await db.UserLogins.AnyAsync(l => l.UserId == user.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Audit_entries_a_deleted_user_acted_in_stay_and_the_delete_is_recorded()
    {
        var (admin, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Admin);
        using var _ = client;
        var user = await factory.CreateUserAsync(BuiltInRole.Contributor);
        var earlier = new AuditEntry
        {
            ActorId = user.Id,
            ActorName = "Gone",
            ActorEmail = user.Email!,
            Action = AuditActions.WorkEdited,
            SubjectType = AuditSubjectTypes.Work,
            SubjectName = "A book",
            Details = "{}",
        };
        await factory.AddRowsAsync(earlier);

        (await DeleteAsync(client, user.Id)).EnsureSuccessStatusCode();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        Assert.True(await db.AuditEntries.AnyAsync(e => e.Id == earlier.Id, TestContext.Current.CancellationToken));
        var entry = await db.AuditEntries.SingleAsync(e => e.Action == AuditActions.UserDeleted && e.SubjectId == user.Id, TestContext.Current.CancellationToken);
        Assert.Equal(admin.Id, entry.ActorId);
        Assert.Equal(AuditSubjectTypes.User, entry.SubjectType);
        using var details = JsonDocument.Parse(entry.Details);
        Assert.Equal(user.Email, details.RootElement.GetProperty("email").GetString());
        Assert.Equal("Contributor", details.RootElement.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Unknown_user_is_not_found()
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        Assert.Equal(HttpStatusCode.NotFound, (await DeleteAsync(admin, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_delete_their_own_account()
    {
        var (admin, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Admin);
        using var _ = client;
        await factory.CreateUserAsync(BuiltInRole.Admin);

        var response = await DeleteAsync(client, admin.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var other = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await other.LoginAsync(admin.Email!, ApiTestExtensions.Password)).StatusCode);
    }

    [Theory]
    [InlineData(BuiltInRole.Contributor)]
    [InlineData(BuiltInRole.Viewer)]
    public async Task Contributor_or_viewer_is_forbidden_from_deleting_a_user(BuiltInRole role)
    {
        using var client = await factory.CreateSignedInClientAsync(role);
        var user = await factory.CreateUserAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteAsync(client, user.Id)).StatusCode);
        using var other = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await other.LoginAsync(user.Email!, ApiTestExtensions.Password)).StatusCode);
    }

    [Fact]
    public async Task Signed_out_caller_cannot_delete_a_user()
    {
        using var client = factory.CreateClient();
        var user = await factory.CreateUserAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await DeleteAsync(client, user.Id)).StatusCode);
        using var other = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await other.LoginAsync(user.Email!, ApiTestExtensions.Password)).StatusCode);
    }

    internal static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Guid id) =>
        client.DeleteAsync($"/api/users/{id}", TestContext.Current.CancellationToken);
}

public class LastAdminDeleteTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task The_only_admin_left_cannot_be_deleted_by_a_session_that_was_an_admin()
    {
        var (a, clientA) = await factory.CreateSignedInUserAsync(BuiltInRole.Admin);
        var (b, clientB) = await factory.CreateSignedInUserAsync(BuiltInRole.Admin);
        using var _a = clientA;
        using var _b = clientB;

        Assert.Equal(HttpStatusCode.NoContent, (await UserRoleApiTests.PutAsync(clientA, b.Id, "viewer")).StatusCode);

        // B's cookie still says admin, though B is a viewer now.
        var response = await UserDeleteApiTests.DeleteAsync(clientB, a.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var other = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await other.LoginAsync(a.Email!, ApiTestExtensions.Password)).StatusCode);
    }
}

public class LastAdminDeleteRaceTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Admins_deleting_each_other_at_once_leave_an_admin()
    {
        var responses = await factory.AdminsActOnEachOtherAtOnceAsync(UserDeleteApiTests.DeleteAsync);

        // A caller who was deleted before their request ran is answered 401.
        Assert.All(responses, r => Assert.True(
            r.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.Conflict or HttpStatusCode.Unauthorized,
            $"Answered {(int)r.StatusCode}."));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        Assert.True(await db.Users.AnyAsync(u => u.RoleId == Role.AdminId, TestContext.Current.CancellationToken));
    }
}
