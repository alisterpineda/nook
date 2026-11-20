using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features.Auth;
using Nook.Api.Features.Setup;

namespace Nook.Api.Tests.Integration;

public class SetupTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    // One test, because setup can only happen once per database and tests in a class share theirs.
    [Fact]
    public async Task Setup_creates_the_first_admin_and_then_closes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();

        Assert.True((await client.GetAsync<SetupStatusResponse>("/api/setup")).Required);

        var tooShort = await client.PostAsJsonAsync(
            "/api/setup", new { email = "admin@example.test", password = "short" }, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Contains("\"Password\"", await tooShort.Content.ReadAsStringAsync(cancellationToken));
        Assert.True((await client.GetAsync<SetupStatusResponse>("/api/setup")).Required);

        var created = await client.PostAsJsonAsync(
            "/api/setup", new { email = "admin@example.test", password = ApiTestExtensions.Password }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var admin = await created.ReadAsync<CurrentUserResponse>();
        Assert.Equal("admin@example.test", admin.Email);
        Assert.Equal(BuiltInRole.Admin, admin.Role);

        // Setup signs the new admin in.
        Assert.Equal(admin, await client.GetAsync<CurrentUserResponse>("/api/auth/me"));

        using var stranger = factory.CreateClient();
        Assert.False((await stranger.GetAsync<SetupStatusResponse>("/api/setup")).Required);

        var again = await stranger.PostAsJsonAsync(
            "/api/setup", new { email = "second@example.test", password = ApiTestExtensions.Password }, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync("/api/auth/me", cancellationToken)).StatusCode);

        // The admin can sign in again with the password they chose.
        Assert.Equal(HttpStatusCode.OK, (await stranger.LoginAsync("admin@example.test", ApiTestExtensions.Password)).StatusCode);
    }
}

public class SetupRaceTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Setups_at_the_same_time_create_one_admin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient(new() { HandleCookies = false });

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => client.PostAsJsonAsync(
            "/api/setup", new { email = $"admin{i}@example.test", password = ApiTestExtensions.Password }, cancellationToken)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(cancellationToken));
    }
}

public class SetupNameTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Setup_stores_the_name_the_first_admin_gave()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();

        var created = await client.PostAsJsonAsync(
            "/api/setup",
            new { email = "named@example.test", password = ApiTestExtensions.Password, displayName = "Nora Admin" },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal("Nora Admin", (await created.ReadAsync<CurrentUserResponse>()).DisplayName);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        Assert.Equal("Nora Admin", (await db.Users.SingleAsync(cancellationToken)).DisplayName);
    }
}
