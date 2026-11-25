using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features.Audit;

namespace Nook.Api.Tests.Integration;

public class UserPasswordApiTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    private const string NewPassword = "a different horse battery";

    [Fact]
    public async Task Password_an_admin_sets_signs_in_and_the_old_one_does_not()
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var user = await factory.CreateUserAsync();

        var response = await PutAsync(admin, user.Id, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var other = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await other.LoginAsync(user.Email!, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.LoginAsync(user.Email!, ApiTestExtensions.Password)).StatusCode);
    }

    [Fact]
    public async Task Admin_can_give_a_password_to_a_user_who_has_none()
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var (user, client) = await factory.CreateSignedInUserWithoutPasswordAsync();
        using var _ = client;

        var response = await PutAsync(admin, user.Id, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var other = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await other.LoginAsync(user.Email!, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Setting_a_password_ends_that_users_sessions()
    {
        await using var app = factory.WithSessionsCheckedOnEveryRequest();
        var admin = await app.CreateUserAsync(BuiltInRole.Admin);
        var user = await app.CreateUserAsync();
        using var adminClient = app.CreateClient();
        using var userClient = app.CreateClient();
        (await adminClient.LoginAsync(admin.Email!, ApiTestExtensions.Password)).EnsureSuccessStatusCode();
        (await userClient.LoginAsync(user.Email!, ApiTestExtensions.Password)).EnsureSuccessStatusCode();

        (await PutAsync(adminClient, user.Id, NewPassword)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await userClient.GetAsync("/api/auth/me", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync("/api/auth/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Password_the_rules_refuse_is_not_set()
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var user = await factory.CreateUserAsync();

        var response = await PutAsync(admin, user.Id, "short-pass1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Password", await response.ErrorFieldsAsync());
        using var other = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await other.LoginAsync(user.Email!, ApiTestExtensions.Password)).StatusCode);
    }

    [Fact]
    public async Task Unknown_user_is_not_found()
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await PutAsync(admin, Guid.NewGuid(), NewPassword);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_set_their_own_password_here()
    {
        var (admin, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Admin);
        using var _ = client;

        var response = await PutAsync(client, admin.Id, NewPassword);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var other = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await other.LoginAsync(admin.Email!, ApiTestExtensions.Password)).StatusCode);
    }

    [Theory]
    [InlineData(BuiltInRole.Contributor)]
    [InlineData(BuiltInRole.Viewer)]
    public async Task Contributor_or_viewer_is_forbidden_from_setting_a_password(BuiltInRole role)
    {
        using var client = await factory.CreateSignedInClientAsync(role);
        var user = await factory.CreateUserAsync();

        var response = await PutAsync(client, user.Id, NewPassword);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var other = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await other.LoginAsync(user.Email!, ApiTestExtensions.Password)).StatusCode);
    }

    [Fact]
    public async Task Signed_out_caller_cannot_set_a_password()
    {
        using var client = factory.CreateClient();
        var user = await factory.CreateUserAsync();

        var response = await PutAsync(client, user.Id, NewPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Password_cannot_be_set_while_password_sign_in_is_off()
    {
        var admin = await factory.CreateUserAsync(BuiltInRole.Admin);
        var user = await factory.CreateUserAsync();
        using var on = factory.CreateClient();
        var sessionCookie = (await on.LoginAsync(admin.Email!, ApiTestExtensions.Password)).SessionCookiePair();

        await using var app = factory.WithSingleSignOn(passwordSignIn: false);
        using var off = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{user.Id}/password")
        {
            Content = JsonContent.Create(new { password = NewPassword }),
        };
        request.Headers.Add("Cookie", sessionCookie);

        var response = await off.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await on.LoginAsync(user.Email!, ApiTestExtensions.Password)).StatusCode);
    }

    [Fact]
    public async Task Setting_a_password_is_recorded_without_the_password()
    {
        var (admin, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Admin);
        using var _ = client;
        var user = await factory.CreateUserAsync();

        (await PutAsync(client, user.Id, NewPassword)).EnsureSuccessStatusCode();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var entry = await db.AuditEntries.SingleAsync(e => e.Action == AuditActions.UserPasswordSet && e.SubjectId == user.Id, TestContext.Current.CancellationToken);
        Assert.Equal(admin.Id, entry.ActorId);
        Assert.Equal(AuditSubjectTypes.User, entry.SubjectType);
        Assert.DoesNotContain(NewPassword, entry.Details);
        Assert.DoesNotContain(ApiTestExtensions.Password, entry.Details);
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, string password) =>
        client.PutAsJsonAsync($"/api/users/{id}/password", new { password }, TestContext.Current.CancellationToken);
}
