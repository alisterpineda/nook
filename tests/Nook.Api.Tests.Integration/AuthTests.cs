using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features.Auth;

namespace Nook.Api.Tests.Integration;

public class AuthTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Theory]
    [InlineData("/api/auth/me")]
    [InlineData("/api/sections")]
    [InlineData("/api/works")]
    [InlineData("/api/works/01a1c7e7-8000-7000-8000-000000000001")]
    [InlineData("/api/people")]
    [InlineData("/api/people/01a1c7e7-8000-7000-8000-000000000001")]
    public async Task Endpoints_refuse_a_client_that_is_not_signed_in(string url)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Logout_refuses_a_client_that_is_not_signed_in()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/auth/logout", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_the_right_password_signs_in()
    {
        var user = await factory.CreateUserAsync(BuiltInRole.Contributor);
        using var client = factory.CreateClient();

        var response = await client.LoginAsync(user.Email!, ApiTestExtensions.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new CurrentUserResponse(user.Id, user.Email!, BuiltInRole.Contributor), await response.ReadAsync<CurrentUserResponse>());
        Assert.Equal(
            new CurrentUserResponse(user.Id, user.Email!, BuiltInRole.Contributor),
            await client.GetAsync<CurrentUserResponse>("/api/auth/me"));
    }

    [Fact]
    public async Task Role_travels_as_its_name_in_camel_case()
    {
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var body = await client.GetStringAsync("/api/auth/me", TestContext.Current.CancellationToken);

        Assert.Contains("\"role\":\"admin\"", body);
    }

    [Fact]
    public async Task Email_with_characters_beyond_letters_and_digits_can_sign_in()
    {
        var email = $"o'brien-josé-{Guid.NewGuid():N}@example.test";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<NookUser>>();
            var created = await users.CreateAsync(new NookUser { UserName = email, Email = email }, ApiTestExtensions.Password);
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        }

        using var client = factory.CreateClient();

        var response = await client.LoginAsync(email, ApiTestExtensions.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(email, (await response.ReadAsync<CurrentUserResponse>()).Email);
    }

    [Fact]
    public async Task Login_with_a_wrong_password_is_refused()
    {
        var user = await factory.CreateUserAsync();
        using var client = factory.CreateClient();

        var response = await client.LoginAsync(user.Email!, "not the password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Login_with_an_unknown_email_is_refused()
    {
        using var client = factory.CreateClient();

        var response = await client.LoginAsync("nobody@example.test", ApiTestExtensions.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_without_an_email_is_a_validation_error()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { password = ApiTestExtensions.Password }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Login_refuses_a_form_post()
    {
        var user = await factory.CreateUserAsync();
        using var client = factory.CreateClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = user.Email!,
            ["password"] = ApiTestExtensions.Password,
        });

        var response = await client.PostAsync("/api/auth/login", form, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.PostAsync("/api/auth/logout", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Account_locks_after_five_wrong_passwords()
    {
        var user = await factory.CreateUserAsync();
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.LoginAsync(user.Email!, "not the password");
        }

        var response = await client.LoginAsync(user.Email!, ApiTestExtensions.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Sign_in_is_rate_limited()
    {
        using var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:Auth:PermitLimit", "3"));
        using var client = limited.CreateClient();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var allowed = await client.LoginAsync("nobody@example.test", "not the password");
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        var refused = await client.LoginAsync("nobody@example.test", "not the password");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }

    [Theory]
    [InlineData(BuiltInRole.Admin)]
    [InlineData(BuiltInRole.Viewer)]
    public async Task A_user_carries_their_role_as_a_claim(BuiltInRole role)
    {
        var user = await factory.CreateUserAsync(role);
        await using var scope = factory.Services.CreateAsyncScope();
        var claims = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<NookUser>>();

        var principal = await claims.CreateAsync(user);

        Assert.Equal([role.ToString()], principal.FindAll(ClaimTypes.Role).Select(c => c.Value));
    }

    [Fact]
    public async Task Health_check_and_api_description_need_no_sign_in()
    {
        using var client = factory.CreateClient();

        var health = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        var description = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Contains("\"/api/works/{id}\"", description);
        Assert.Contains("\"epub\"", description);
    }

    [Fact]
    public async Task Sign_in_options_name_no_single_sign_on_when_none_is_configured()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/sign-in-options", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "{\"ssoName\":null}",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Single_sign_on_login_is_not_found_when_none_is_configured()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/oidc/login", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Authentik", "Authentik")]
    [InlineData(null, "SSO")]
    public async Task Sign_in_options_name_the_configured_single_sign_on(string? displayName, string expected)
    {
        await using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Nook:Oidc:Authority", "https://sso.test/realms/nook");
            builder.UseSetting("Nook:Oidc:ClientId", "nook");
            builder.UseSetting("Nook:Oidc:ClientSecret", "nook-secret");
            if (displayName is not null)
            {
                builder.UseSetting("Nook:Oidc:DisplayName", displayName);
            }
        });
        using var client = app.CreateClient();

        var options = await client.GetAsync<SignInOptionsResponse>("/api/auth/sign-in-options");

        Assert.Equal(new SignInOptionsResponse(expected), options);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("nook")]
    public async Task Api_refuses_to_start_with_an_authority_but_no_client_id_or_secret(string? clientId)
    {
        await using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Nook:Oidc:Authority", "https://sso.test/realms/nook");
            if (clientId is not null)
            {
                builder.UseSetting("Nook:Oidc:ClientId", clientId);
            }
        });

        var exception = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        var messages = exception.Flatten().Select(inner => inner.Message);
        Assert.Contains(messages, message => message.Contains("Nook:Oidc", StringComparison.Ordinal));
    }
}
