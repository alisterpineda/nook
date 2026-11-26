using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features;
using Nook.Api.Features.Audit;
using Nook.Api.Features.Auth;
using Nook.Api.Features.Users;

namespace Nook.Api.Tests.Integration;

public class UserAddApiTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    private const string NewPassword = "a different horse battery";

    private static string NewEmail() => $"{Guid.NewGuid():N}@example.test";

    [Theory]
    [InlineData(BuiltInRole.Admin, "admin")]
    [InlineData(BuiltInRole.Contributor, "contributor")]
    [InlineData(BuiltInRole.Viewer, "viewer")]
    public async Task Added_user_signs_in_with_the_password_and_holds_the_role(BuiltInRole role, string roleName)
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var email = NewEmail();

        var response = await admin.PostAsJsonAsync("/api/users", new { email, displayName = "Grace Hopper", role = roleName, password = NewPassword }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var added = await response.ReadAsync<UserSummaryResponse>();
        Assert.Equal(email, added.Email);
        Assert.Equal("Grace Hopper", added.Name);
        Assert.Equal(role.ToString(), added.Role);
        Assert.True(added.HasPassword);

        using var newcomer = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await newcomer.LoginAsync(email, NewPassword)).StatusCode);
        var me = await newcomer.GetAsync<CurrentUserResponse>("/api/auth/me");
        Assert.Equal(role, me.Role);
        Assert.Equal("Grace Hopper", me.DisplayName);

        var listed = await admin.GetAsync<Page<UserSummaryResponse>>($"/api/users?limit=200&sort=joined&direction=desc");
        Assert.Contains(listed.Items, u => u.Id == added.Id);
    }

    [Fact]
    public async Task Email_already_in_use_adds_nobody()
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var existing = await factory.CreateUserAsync();

        var response = await PostAsync(admin, new { email = existing.Email, role = "viewer", password = NewPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Email", await response.ErrorFieldsAsync());
        Assert.Equal(1, await CountUsersAsync(existing.Email!));
    }

    [Fact]
    public async Task Password_the_rules_refuse_adds_nobody()
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var email = NewEmail();

        var response = await PostAsync(admin, new { email, role = "viewer", password = "short-pass1" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Password", await response.ErrorFieldsAsync());
        Assert.Equal(0, await CountUsersAsync(email));
    }

    [Fact]
    public async Task Anonymous_role_adds_nobody()
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var email = NewEmail();

        var response = await PostAsync(admin, new { email, role = "anonymous", password = NewPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Role", await response.ErrorFieldsAsync());
        Assert.Equal(0, await CountUsersAsync(email));
    }

    [Fact]
    public async Task Number_that_names_no_role_adds_nobody()
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var email = NewEmail();

        var response = await PostAsync(admin, new { email, role = 7, password = NewPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Role", await response.ErrorFieldsAsync());
        Assert.Equal(0, await CountUsersAsync(email));
    }

    [Fact]
    public async Task Request_with_no_role_adds_nobody()
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var email = NewEmail();

        var response = await PostAsync(admin, new { email, password = NewPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Role", await response.ErrorFieldsAsync());
        Assert.Equal(0, await CountUsersAsync(email));
    }

    [Theory]
    [InlineData(BuiltInRole.Contributor)]
    [InlineData(BuiltInRole.Viewer)]
    public async Task Contributor_or_viewer_is_forbidden_from_adding_a_user(BuiltInRole role)
    {
        using var client = await factory.CreateSignedInClientAsync(role);
        var email = NewEmail();

        var response = await PostAsync(client, new { email, role = "viewer", password = NewPassword });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await CountUsersAsync(email));
    }

    [Fact]
    public async Task Signed_out_caller_cannot_add_a_user()
    {
        using var client = factory.CreateClient();
        var email = NewEmail();

        var response = await PostAsync(client, new { email, role = "viewer", password = NewPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await CountUsersAsync(email));
    }

    [Fact]
    public async Task User_cannot_be_added_while_password_sign_in_is_off()
    {
        var admin = await factory.CreateUserAsync(BuiltInRole.Admin);
        using var on = factory.CreateClient();
        var sessionCookie = (await on.LoginAsync(admin.Email!, ApiTestExtensions.Password)).SessionCookiePair();
        var email = NewEmail();

        await using var app = factory.WithSingleSignOn(passwordSignIn: false);
        using var off = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users")
        {
            Content = JsonContent.Create(new { email, role = "viewer", password = NewPassword }),
        };
        request.Headers.Add("Cookie", sessionCookie);

        var response = await off.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await CountUsersAsync(email));
    }

    [Fact]
    public async Task Adding_a_user_is_recorded_without_the_password()
    {
        var (admin, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Admin);
        using var _ = client;
        var email = NewEmail();

        var added = await (await PostAsync(client, new { email, role = "contributor", password = NewPassword })).ReadAsync<UserSummaryResponse>();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var entry = await db.AuditEntries.SingleAsync(e => e.Action == AuditActions.UserAdded && e.SubjectId == added.Id, TestContext.Current.CancellationToken);
        Assert.Equal(admin.Id, entry.ActorId);
        Assert.Equal(AuditSubjectTypes.User, entry.SubjectType);
        using var details = JsonDocument.Parse(entry.Details);
        Assert.Equal(email, details.RootElement.GetProperty("email").GetString());
        Assert.Equal("Contributor", details.RootElement.GetProperty("role").GetString());
        Assert.DoesNotContain(NewPassword, entry.Details);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, object body) =>
        client.PostAsJsonAsync("/api/users", body, TestContext.Current.CancellationToken);

    private async Task<int> CountUsersAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        return await db.Users.CountAsync(u => u.Email == email, TestContext.Current.CancellationToken);
    }
}
