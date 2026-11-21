using System.Net;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nook.Api.Data;
using Nook.Api.Features.Auth;
using Nook.Api.Features.Setup;

namespace Nook.Api.Tests.Integration;

/// <summary>Single sign-on through a real OpenID Connect provider, a Keycloak with the realm <c>nook</c>.</summary>
public class OidcSignInTests(NookApiFactory factory, KeycloakFixture keycloak, PostgresFixture postgres)
    : IClassFixture<NookApiFactory>, IClassFixture<KeycloakFixture>, IAsyncLifetime
{
    private readonly Dictionary<(string? AdminGroup, string? ContributorGroup), WebApplicationFactory<Program>> _apps = [];

    [Fact]
    public async Task Single_sign_on_login_redirects_to_the_provider_with_a_code_request()
    {
        using var client = CreateClient(App());

        var response = await client.GetAsync("/api/auth/oidc/login", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.StartsWith(
            $"{keycloak.Authority}/protocol/openid-connect/auth",
            location.GetLeftPart(UriPartial.Path),
            StringComparison.Ordinal);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("nook", query["client_id"]);
        Assert.Equal("openid profile email", query["scope"]);
        Assert.False(string.IsNullOrEmpty(query["code_challenge"]));
        Assert.Equal("https://localhost/api/auth/oidc/callback", query["redirect_uri"]);
    }

    [Fact]
    public async Task Single_sign_on_login_returns_to_sign_in_when_the_provider_cannot_be_reached()
    {
        await using var app = CreateApp(factory, "https://localhost:1/", adminGroup: null, contributorGroup: null);
        using var client = CreateClient(app);

        var response = await client.GetAsync("/api/auth/oidc/login", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/sign-in?error=sso-failed", response.Headers.Location?.OriginalString);
        Assert.False(response.SetsSessionCookie());
        Assert.Contains(app.ReadLogs(LogLevel.Warning), message => message.Contains("single sign-on", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task First_single_sign_on_creates_the_user_and_signs_them_in()
    {
        using var client = CreateClient(App());

        var response = await keycloak.SignInAsync(client, "fay");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.True(response.SetsSessionCookie());
        var me = await client.GetAsync<CurrentUserResponse>("/api/auth/me");
        Assert.Equal("fay@sso.test", me.Email);
        var user = await FindUserAsync("fay@sso.test");
        Assert.Equal(me.Id, user.Id);
        Assert.Null(user.PasswordHash);
    }

    [Fact]
    public async Task Single_sign_on_again_is_the_same_user()
    {
        using var first = CreateClient(App());
        using var second = CreateClient(App());

        await keycloak.SignInAsync(first, "ren");
        await keycloak.SignInAsync(second, "ren");

        var firstMe = await first.GetAsync<CurrentUserResponse>("/api/auth/me");
        var secondMe = await second.GetAsync<CurrentUserResponse>("/api/auth/me");
        Assert.Equal(firstMe.Id, secondMe.Id);
        await using var scope = App().Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        Assert.Equal(
            1,
            await db.Users.CountAsync(u => u.Email == "ren@sso.test", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_new_user_with_an_unverified_email_is_created()
    {
        using var client = CreateClient(App());

        var response = await keycloak.SignInAsync(client, "una");

        Assert.Equal("/", response.Headers.Location?.OriginalString);
        var me = await client.GetAsync<CurrentUserResponse>("/api/auth/me");
        Assert.Equal("una@sso.test", me.Email);
        Assert.False((await FindUserAsync("una@sso.test")).EmailConfirmed);
    }

    [Fact]
    public async Task Logout_ends_a_single_sign_on_session_without_a_redirect()
    {
        using var client = CreateClient(App());
        await keycloak.SignInAsync(client, "sol");
        await client.GetAsync<CurrentUserResponse>("/api/auth/me");

        var response = await client.PostAsync("/api/auth/logout", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(response.Headers.Location);
        var me = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Role_comes_from_the_provider_groups()
    {
        var app = App(adminGroup: "nook-admins", contributorGroup: "nook-contributors");

        Assert.Equal(BuiltInRole.Admin, (await SignInAsync(app, "ada")).Role);
        Assert.Equal(BuiltInRole.Contributor, (await SignInAsync(app, "mel")).Role);
        Assert.Equal(BuiltInRole.Admin, (await SignInAsync(app, "bo")).Role);
        Assert.Equal(BuiltInRole.Viewer, (await SignInAsync(app, "gus")).Role);
    }

    [Fact]
    public async Task Role_is_set_again_on_the_next_single_sign_on()
    {
        var first = await SignInAsync(App(adminGroup: "nook-admins", contributorGroup: "nook-contributors"), "rae");
        Assert.Equal(BuiltInRole.Admin, first.Role);

        var next = await SignInAsync(App(adminGroup: "nobody", contributorGroup: "nook-admins"), "rae");

        Assert.Equal(first.Id, next.Id);
        Assert.Equal(BuiltInRole.Contributor, next.Role);
    }

    [Fact]
    public async Task A_new_user_is_a_viewer_when_no_group_is_named()
    {
        var me = await SignInAsync(App(), "nia");

        Assert.Equal(BuiltInRole.Viewer, me.Role);
    }

    [Fact]
    public async Task The_member_group_setting_is_ignored()
    {
        await using var app = CreateApp(factory, keycloak.Authority, adminGroup: null, contributorGroup: null)
            .WithWebHostBuilder(builder => builder.UseSetting("Nook:Oidc:MemberGroup", "nook-contributors"));

        var me = await SignInAsync(app, "cora");

        Assert.Equal(BuiltInRole.Viewer, me.Role);
    }

    [Fact]
    public async Task A_verified_email_links_to_the_user_who_has_it()
    {
        var app = App(adminGroup: "nook-admins", contributorGroup: "nook-contributors");
        var user = await app.CreateUserAsync(BuiltInRole.Admin, "lin@sso.test");

        var me = await SignInAsync(app, "lin");

        Assert.Equal(user.Id, me.Id);
        Assert.Equal(BuiltInRole.Contributor, me.Role);
        using var passwordClient = CreateClient(app);
        var login = await passwordClient.LoginAsync("lin@sso.test", ApiTestExtensions.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(user.Id, (await SignInAsync(app, "lin")).Id);
        var logins = await GetLoginsAsync(app, user.Id);
        Assert.Equal(OidcSignIn.Scheme, Assert.Single(logins).LoginProvider);
    }

    [Fact]
    public async Task Single_sign_on_gives_a_new_user_the_name_the_provider_sent()
    {
        var me = await SignInAsync(App(), "fay");

        Assert.Equal("Fay First", me.DisplayName);
        Assert.Equal("Fay First", (await FindUserAsync("fay@sso.test")).DisplayName);
    }

    [Fact]
    public async Task Single_sign_on_names_an_existing_user_who_has_no_name_and_keeps_a_name_already_set()
    {
        var app = App();
        await app.CreateUserAsync(BuiltInRole.Contributor, "dee@sso.test");
        await app.CreateUserAsync(BuiltInRole.Contributor, "eli@sso.test", displayName: "Eli Renamed");

        var unnamed = await SignInAsync(app, "dee");
        var named = await SignInAsync(app, "eli");

        Assert.Equal("Dee Nameless", unnamed.DisplayName);
        Assert.Equal("Eli Renamed", named.DisplayName);
        Assert.Equal("Eli Renamed", (await FindUserAsync("eli@sso.test")).DisplayName);
    }

    [Fact]
    public async Task An_existing_role_is_kept_when_no_group_is_named()
    {
        var app = App();
        var user = await app.CreateUserAsync(BuiltInRole.Contributor, "kit@sso.test");

        var me = await SignInAsync(app, "kit");

        Assert.Equal(user.Id, me.Id);
        Assert.Equal(BuiltInRole.Contributor, me.Role);
    }

    [Fact]
    public async Task An_unverified_email_does_not_link()
    {
        var app = App();
        var user = await app.CreateUserAsync(BuiltInRole.Contributor, "uma@sso.test");
        using var client = CreateClient(app);

        var response = await keycloak.SignInAsync(client, "uma");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/sign-in?error=sso-email-unverified", response.Headers.Location?.OriginalString);
        Assert.False(response.SetsSessionCookie());
        var me = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Empty(await GetLoginsAsync(app, user.Id));
    }

    [Fact]
    public async Task Someone_without_an_email_is_refused()
    {
        var app = App();
        var before = await CountUsersAsync(app);
        using var client = CreateClient(app);

        var response = await keycloak.SignInAsync(client, "noe");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/sign-in?error=sso-no-email", response.Headers.Location?.OriginalString);
        Assert.False(response.SetsSessionCookie());
        Assert.Equal(before, await CountUsersAsync(app));
    }

    [Fact]
    public async Task A_verified_email_does_not_link_to_a_user_another_provider_account_has()
    {
        var app = App();
        var user = await app.CreateUserAsync(BuiltInRole.Contributor, "ivy@sso.test");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<NookUser>>();
            var stored = await users.FindByIdAsync(user.Id.ToString());
            var added = await users.AddLoginAsync(stored!, new UserLoginInfo(OidcSignIn.Scheme, "someone-else", OidcSignIn.Scheme));
            Assert.True(added.Succeeded);
        }

        using var client = CreateClient(app);

        var response = await keycloak.SignInAsync(client, "ivy");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/sign-in?error=sso-failed", response.Headers.Location?.OriginalString);
        Assert.False(response.SetsSessionCookie());
        Assert.Equal("someone-else", Assert.Single(await GetLoginsAsync(app, user.Id)).ProviderKey);
    }

    [Fact]
    public async Task Someone_who_is_not_an_admin_cannot_be_the_first_user()
    {
        await using var nook = await CreateEmptyNookAsync(adminGroup: "nook-admins");
        var app = nook.App;
        using var client = CreateClient(app);

        var response = await keycloak.SignInAsync(client, "zed");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/sign-in?error=sso-setup-required", response.Headers.Location?.OriginalString);
        Assert.False(response.SetsSessionCookie());
        Assert.Equal(0, await CountUsersAsync(app));
        Assert.True((await client.GetAsync<SetupStatusResponse>("/api/setup")).Required);
    }

    [Fact]
    public async Task Nobody_can_be_the_first_user_when_no_admin_group_is_named()
    {
        await using var nook = await CreateEmptyNookAsync(adminGroup: null);
        var app = nook.App;
        using var client = CreateClient(app);

        // amy is in the provider's admin group, which this app does not name.
        var response = await keycloak.SignInAsync(client, "amy");

        Assert.Equal("/sign-in?error=sso-setup-required", response.Headers.Location?.OriginalString);
        Assert.Equal(0, await CountUsersAsync(app));
    }

    [Fact]
    public async Task An_admin_from_the_provider_groups_can_be_the_first_user()
    {
        await using var nook = await CreateEmptyNookAsync(adminGroup: "nook-admins");
        var app = nook.App;
        using var client = CreateClient(app);

        var response = await keycloak.SignInAsync(client, "amy");

        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.Equal(BuiltInRole.Admin, (await client.GetAsync<CurrentUserResponse>("/api/auth/me")).Role);
        Assert.False((await client.GetAsync<SetupStatusResponse>("/api/setup")).Required);
    }

    [Fact]
    public async Task An_admin_from_the_provider_groups_can_be_the_first_user_when_password_sign_in_is_off()
    {
        await using var nook = await CreateEmptyNookAsync(adminGroup: "nook-admins", passwordSignIn: false);
        var app = nook.App;
        using var client = CreateClient(app);

        var response = await keycloak.SignInAsync(client, "amy");

        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.Equal(BuiltInRole.Admin, (await client.GetAsync<CurrentUserResponse>("/api/auth/me")).Role);
        Assert.Equal(1, await CountUsersAsync(app));
    }

    [Fact]
    public async Task Someone_who_is_not_an_admin_cannot_be_the_first_user_when_password_sign_in_is_off()
    {
        await using var nook = await CreateEmptyNookAsync(adminGroup: "nook-admins", passwordSignIn: false);
        var app = nook.App;
        using var client = CreateClient(app);

        var response = await keycloak.SignInAsync(client, "zed");

        Assert.Equal("/sign-in?error=sso-setup-required", response.Headers.Location?.OriginalString);
        Assert.False(response.SetsSessionCookie());
        Assert.Equal(0, await CountUsersAsync(app));
    }

    [Fact]
    public async Task A_callback_the_provider_did_not_issue_returns_to_sign_in()
    {
        var app = App();
        using var client = CreateClient(app);
        var warnings = app.ReadLogs(LogLevel.Warning).Count;

        var response = await client.GetAsync(
            "/api/auth/oidc/callback?error=access_denied&state=not-a-state", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/sign-in?error=sso-failed", response.Headers.Location?.OriginalString);
        Assert.False(response.SetsSessionCookie());
        Assert.True(app.ReadLogs(LogLevel.Warning).Count > warnings, "No warning was logged.");
    }

    /// <summary>
    /// Gives the class's database a user, so that it is past first-run setup and a test's single sign-on is not
    /// the first user whichever test runs first.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        if (await CountUsersAsync(factory) == 0)
        {
            await factory.CreateUserAsync(BuiltInRole.Admin);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var app in _apps.Values)
        {
            await app.DisposeAsync();
        }
    }

    /// <summary>A Nook that signs in through the Keycloak, kept for the class and shared by tests that name the same groups.</summary>
    private WebApplicationFactory<Program> App(string? adminGroup = null, string? contributorGroup = null)
    {
        if (!_apps.TryGetValue((adminGroup, contributorGroup), out var app))
        {
            app = CreateApp(factory, keycloak.Authority, adminGroup, contributorGroup);
            _apps[(adminGroup, contributorGroup)] = app;
        }

        return app;
    }

    private static WebApplicationFactory<Program> CreateApp(
        NookApiFactory nook, string authority, string? adminGroup, string? contributorGroup, bool passwordSignIn = true) =>
        nook.WithWebHostBuilder(builder =>
        {
            if (!passwordSignIn)
            {
                builder.UseSetting(PasswordSignInOptions.Setting, "false");
            }

            builder.UseSetting("Nook:Oidc:Authority", authority);
            builder.UseSetting("Nook:Oidc:ClientId", "nook");
            builder.UseSetting("Nook:Oidc:ClientSecret", "nook-test-secret");
            if (adminGroup is not null)
            {
                builder.UseSetting("Nook:Oidc:AdminGroup", adminGroup);
            }

            if (contributorGroup is not null)
            {
                builder.UseSetting("Nook:Oidc:ContributorGroup", contributorGroup);
            }

            // Keycloak answers over plain http. The https check runs in the handler's own post-configure step,
            // so this must be Configure, which runs before it.
            builder.ConfigureServices(services =>
                services.Configure<OpenIdConnectOptions>(OidcSignIn.Scheme, options => options.RequireHttpsMetadata = false));
        });

    /// <summary>A Nook with a database of its own that has no users, signing in through the Keycloak.</summary>
    private async Task<EmptyNook> CreateEmptyNookAsync(string? adminGroup, bool passwordSignIn = true)
    {
        var empty = new NookApiFactory(postgres);
        await empty.InitializeAsync();
        return new EmptyNook(empty, CreateApp(empty, keycloak.Authority, adminGroup, contributorGroup: null, passwordSignIn));
    }

    /// <summary>An app and the factory that holds its database, disposed together.</summary>
    private sealed record EmptyNook(NookApiFactory Factory, WebApplicationFactory<Program> App) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            await Factory.DisposeAsync();
        }
    }

    /// <summary>
    /// A client of Nook over https, so the handler's secure cookies are kept, that does not follow redirects,
    /// so a test sees where Nook sends it.
    /// </summary>
    private static HttpClient CreateClient(WebApplicationFactory<Program> app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    /// <summary>Signs in to an app as a realm user through Keycloak, and answers who Nook says is signed in.</summary>
    private async Task<CurrentUserResponse> SignInAsync(WebApplicationFactory<Program> app, string username)
    {
        using var client = CreateClient(app);
        var response = await keycloak.SignInAsync(client, username);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        return await client.GetAsync<CurrentUserResponse>("/api/auth/me");
    }

    private static async Task<IList<UserLoginInfo>> GetLoginsAsync(WebApplicationFactory<Program> app, Guid userId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<NookUser>>();
        var user = await users.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        return await users.GetLoginsAsync(user);
    }

    private static async Task<int> CountUsersAsync(WebApplicationFactory<Program> app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        return await db.Users.CountAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The stored user with an email, read fresh from the database.</summary>
    private async Task<NookUser> FindUserAsync(string email)
    {
        await using var scope = App().Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<NookUser>>();
        var user = await users.FindByEmailAsync(email);
        Assert.NotNull(user);
        return user;
    }
}
