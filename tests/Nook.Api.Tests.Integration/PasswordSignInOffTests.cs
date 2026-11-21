using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features.Auth;
using Nook.Api.Features.Setup;

namespace Nook.Api.Tests.Integration;

public class PasswordSignInOffTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Login_is_not_found_when_password_sign_in_is_off()
    {
        var user = await factory.CreateUserAsync();
        await using var app = factory.WithSingleSignOn(passwordSignIn: false);
        using var client = app.CreateClient();

        foreach (var response in new[]
        {
            await client.LoginAsync(user.Email!, ApiTestExtensions.Password),
            await client.LoginAsync(user.Email!, "not the password"),
            await client.PostAsJsonAsync("/api/auth/login", new { }, TestContext.Current.CancellationToken),
        })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.False(response.SetsSessionCookie());
        }
    }

    [Fact]
    public async Task A_password_refused_while_sign_in_was_off_works_once_it_is_on_again()
    {
        var user = await factory.CreateUserAsync();
        await using var app = factory.WithSingleSignOn(passwordSignIn: false);
        using var off = app.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await off.LoginAsync(user.Email!, ApiTestExtensions.Password)).StatusCode);

        using var on = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await on.LoginAsync(user.Email!, ApiTestExtensions.Password)).StatusCode);
    }

    [Fact]
    public async Task A_session_started_with_a_password_outlives_turning_password_sign_in_off()
    {
        var user = await factory.CreateUserAsync();
        using var on = factory.CreateClient();
        var login = await on.LoginAsync(user.Email!, ApiTestExtensions.Password);
        login.EnsureSuccessStatusCode();
        var sessionCookie = login.SessionCookiePair();

        await using var app = factory.WithSingleSignOn(passwordSignIn: false);
        using var off = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("Cookie", sessionCookie);

        var response = await off.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(user.Id, (await response.ReadAsync<CurrentUserResponse>()).Id);
    }

    [Fact]
    public async Task Sign_in_options_say_password_sign_in_is_off()
    {
        await using var app = factory.WithSingleSignOn(passwordSignIn: false);
        using var client = app.CreateClient();

        var options = await client.GetAsync<SignInOptionsResponse>("/api/auth/sign-in-options");

        Assert.Equal(new SignInOptionsResponse("SSO", false), options);
    }
}

public class PasswordSignInOffSetupTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Setup_is_closed_on_an_empty_nook_when_password_sign_in_is_off()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = factory.WithSingleSignOn(passwordSignIn: false);
        using var client = app.CreateClient();

        Assert.False((await client.GetAsync<SetupStatusResponse>("/api/setup")).Required);

        var created = await client.PostAsJsonAsync(
            "/api/setup", new { email = "admin@example.test", password = ApiTestExtensions.Password }, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, created.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            Assert.Equal(0, await db.Users.CountAsync(cancellationToken));
        }

        using var open = factory.CreateClient();
        Assert.True((await open.GetAsync<SetupStatusResponse>("/api/setup")).Required);
    }
}
