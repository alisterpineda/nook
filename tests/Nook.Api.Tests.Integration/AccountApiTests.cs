using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features.Account;
using Nook.Api.Features.Audit;
using Nook.Api.Features.Auth;

namespace Nook.Api.Tests.Integration;

public class AccountApiTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    private const string NewPassword = "a different horse battery";

    [Fact]
    public async Task Account_tells_a_user_their_email_name_role_and_how_they_sign_in()
    {
        var (user, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Viewer, displayName: "Ada Lovelace");
        using var _ = client;

        var account = await client.GetAsync<AccountResponse>("/api/account");

        Assert.Equal(new AccountResponse(user.Email!, "Ada Lovelace", "Viewer", true, false), account);

        var (_, ssoClient) = await factory.CreateSignedInUserWithoutPasswordAsync();
        using var __ = ssoClient;

        var ssoAccount = await ssoClient.GetAsync<AccountResponse>("/api/account");

        Assert.False(ssoAccount.HasPassword);
        Assert.True(ssoAccount.HasSingleSignOn);
    }

    [Fact]
    public async Task Signed_out_caller_is_not_told_about_an_account()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/account", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Changing_a_password_makes_the_new_one_sign_in_and_the_old_one_not()
    {
        var (user, client) = await factory.CreateSignedInUserAsync();
        using var _ = client;

        var changed = await PutPasswordAsync(client, ApiTestExtensions.Password, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        using var other = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await other.LoginAsync(user.Email!, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.LoginAsync(user.Email!, ApiTestExtensions.Password)).StatusCode);
    }

    [Fact]
    public async Task Wrong_current_password_changes_nothing()
    {
        var (user, client) = await factory.CreateSignedInUserAsync();
        using var _ = client;

        var response = await PutPasswordAsync(client, "not the password", NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("CurrentPassword", await ErrorFieldsAsync(response));
        using var other = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await other.LoginAsync(user.Email!, ApiTestExtensions.Password)).StatusCode);
    }

    [Fact]
    public async Task Missing_current_password_changes_nothing()
    {
        var (user, client) = await factory.CreateSignedInUserAsync();
        using var _ = client;

        var response = await PutPasswordAsync(client, null, NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("CurrentPassword", await ErrorFieldsAsync(response));
        using var other = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await other.LoginAsync(user.Email!, ApiTestExtensions.Password)).StatusCode);
    }

    [Fact]
    public async Task New_password_the_rules_refuse_changes_nothing()
    {
        var (user, client) = await factory.CreateSignedInUserAsync();
        using var _ = client;

        var response = await PutPasswordAsync(client, ApiTestExtensions.Password, "short-pass1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("NewPassword", await ErrorFieldsAsync(response));
        using var other = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await other.LoginAsync(user.Email!, ApiTestExtensions.Password)).StatusCode);
    }

    [Fact]
    public async Task User_with_no_password_adds_one_without_a_current_password()
    {
        var (user, client) = await factory.CreateSignedInUserWithoutPasswordAsync();
        using var _ = client;

        var response = await client.PutAsJsonAsync("/api/account/password", new { newPassword = NewPassword }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var other = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await other.LoginAsync(user.Email!, NewPassword)).StatusCode);
        Assert.True((await client.GetAsync<AccountResponse>("/api/account")).HasPassword);
    }

    [Fact]
    public async Task Changing_a_password_keeps_this_session_and_ends_the_others()
    {
        await using var app = factory.WithSessionsCheckedOnEveryRequest();
        var user = await app.CreateUserAsync();
        using var changing = app.CreateClient();
        using var other = app.CreateClient();
        (await changing.LoginAsync(user.Email!, ApiTestExtensions.Password)).EnsureSuccessStatusCode();
        (await other.LoginAsync(user.Email!, ApiTestExtensions.Password)).EnsureSuccessStatusCode();

        (await PutPasswordAsync(changing, ApiTestExtensions.Password, NewPassword)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await changing.GetAsync("/api/auth/me", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/auth/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Password_cannot_be_changed_while_password_sign_in_is_off()
    {
        var user = await factory.CreateUserAsync();
        using var on = factory.CreateClient();
        var sessionCookie = (await on.LoginAsync(user.Email!, ApiTestExtensions.Password)).SessionCookiePair();

        await using var app = factory.WithSingleSignOn(passwordSignIn: false);
        using var off = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/account/password")
        {
            Content = JsonContent.Create(new { currentPassword = ApiTestExtensions.Password, newPassword = NewPassword }),
        };
        request.Headers.Add("Cookie", sessionCookie);

        var response = await off.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await on.LoginAsync(user.Email!, ApiTestExtensions.Password)).StatusCode);
    }

    [Fact]
    public async Task Changing_a_password_is_recorded_without_the_password()
    {
        var (user, client) = await factory.CreateSignedInUserAsync();
        using var _ = client;

        (await PutPasswordAsync(client, ApiTestExtensions.Password, NewPassword)).EnsureSuccessStatusCode();

        var entry = Assert.Single(await EntriesAsync(user.Id));
        Assert.Equal(user.Id, entry.ActorId);
        Assert.Equal(user.Id, entry.SubjectId);
        Assert.Equal(AuditSubjectTypes.User, entry.SubjectType);
        Assert.DoesNotContain(ApiTestExtensions.Password, entry.Details);
        Assert.DoesNotContain(NewPassword, entry.Details);
    }

    [Fact]
    public async Task Adding_a_password_is_recorded()
    {
        var (user, client) = await factory.CreateSignedInUserWithoutPasswordAsync();
        using var _ = client;

        (await client.PutAsJsonAsync("/api/account/password", new { newPassword = NewPassword }, TestContext.Current.CancellationToken))
            .EnsureSuccessStatusCode();

        var entry = Assert.Single(await EntriesAsync(user.Id));
        using var details = JsonDocument.Parse(entry.Details);
        Assert.True(details.RootElement.GetProperty("added").GetBoolean());
        Assert.DoesNotContain(NewPassword, entry.Details);
    }

    [Fact]
    public async Task Refused_password_change_is_not_recorded()
    {
        var (user, client) = await factory.CreateSignedInUserAsync();
        using var _ = client;

        var response = await PutPasswordAsync(client, "not the password", NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await EntriesAsync(user.Id));
    }

    private static Task<HttpResponseMessage> PutPasswordAsync(HttpClient client, string? currentPassword, string newPassword) =>
        client.PutAsJsonAsync("/api/account/password", new { currentPassword, newPassword }, TestContext.Current.CancellationToken);

    private static async Task<List<string>> ErrorFieldsAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return [.. body.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name)];
    }

    private async Task<List<AuditEntry>> EntriesAsync(Guid actorId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        return await db.AuditEntries
            .Where(e => e.Action == AuditActions.UserPasswordChanged && e.ActorId == actorId)
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
