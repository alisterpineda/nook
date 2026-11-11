using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;

namespace Nook.Api.Tests.Integration;

/// <summary>What tests that call the API over HTTP share: users, signed-in clients and reading responses.</summary>
internal static class ApiTestExtensions
{
    public const string Password = "correct horse battery";

    /// <summary>Reads JSON the way the API writes it: camelCase names, and enums by name.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Creates a user with a new email and <see cref="Password"/>.</summary>
    public static async Task<NookUser> CreateUserAsync(this WebApplicationFactory<Program> factory, UserRole role = UserRole.Member)
    {
        var email = $"{Guid.NewGuid():N}@example.test";
        var user = new NookUser { UserName = email, Email = email, Role = role };

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<NookUser>>();
        var result = await users.CreateAsync(user, Password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));

        return user;
    }

    /// <summary>A client that holds the session cookie of a new user.</summary>
    public static async Task<HttpClient> CreateSignedInClientAsync(
        this WebApplicationFactory<Program> factory, UserRole role = UserRole.Member)
    {
        var user = await factory.CreateUserAsync(role);
        var client = factory.CreateClient();

        var response = await client.LoginAsync(user.Email!, Password);
        response.EnsureSuccessStatusCode();

        return client;
    }

    public static Task<HttpResponseMessage> LoginAsync(this HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password }, TestContext.Current.CancellationToken);

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<T>(Json, TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        return body;
    }

    public static async Task<T> GetAsync<T>(this HttpClient client, string url)
    {
        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"GET {url} answered {(int)response.StatusCode}.");
        return await response.ReadAsync<T>();
    }
}
