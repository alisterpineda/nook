using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    /// <summary>The name of the session cookie.</summary>
    private const string SessionCookie = "nook";

    /// <summary>Creates a user with <see cref="Password"/>, and a new email unless one is given.</summary>
    public static async Task<NookUser> CreateUserAsync(
        this WebApplicationFactory<Program> factory, BuiltInRole role = BuiltInRole.Contributor, string? email = null)
    {
        email ??= $"{Guid.NewGuid():N}@example.test";
        var user = new NookUser { UserName = email, Email = email, RoleId = Role.IdOf(role) };

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<NookUser>>();
        var result = await users.CreateAsync(user, Password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));

        return user;
    }

    /// <summary>A client that holds the session cookie of a new user.</summary>
    public static async Task<HttpClient> CreateSignedInClientAsync(
        this WebApplicationFactory<Program> factory, BuiltInRole role = BuiltInRole.Contributor)
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

    /// <summary>The cookies a response sets, each as its whole <c>Set-Cookie</c> value.</summary>
    public static IEnumerable<string> SetCookies(this HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [];

    /// <summary>The cookies a response sets, each as the <c>name=value</c> pair a request sends back in its <c>Cookie</c> header.</summary>
    public static IEnumerable<string> CookiePairs(this HttpResponseMessage response) =>
        response.SetCookies().Select(cookie => cookie.Split(';')[0]);

    /// <summary>Whether a response sets the session cookie.</summary>
    public static bool SetsSessionCookie(this HttpResponseMessage response) =>
        response.CookiePairs().Any(IsSessionCookie);

    /// <summary>The session cookie a response sets, as its <c>name=value</c> pair.</summary>
    public static string SessionCookiePair(this HttpResponseMessage response) =>
        response.CookiePairs().Single(IsSessionCookie);

    private static bool IsSessionCookie(string pair) =>
        pair.StartsWith($"{SessionCookie}=", StringComparison.Ordinal);

    /// <summary>An exception with every exception inside it, for finding the message of a failed start.</summary>
    public static IEnumerable<Exception> Flatten(this Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.SelectMany(Flatten))
                {
                    yield return inner;
                }
            }
        }
    }

    /// <summary>Saves rows straight to the database, for what no endpoint can set up.</summary>
    public static async Task AddRowsAsync(this WebApplicationFactory<Program> factory, params object[] rows)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        db.AddRange(rows);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The messages the API has logged at a level so far, oldest first.</summary>
    public static IReadOnlyList<string> ReadLogs(this WebApplicationFactory<Program> factory, LogLevel level) =>
        factory.Services.GetFakeLogCollector().GetSnapshot()
            .Where(r => r.Level == level)
            .Select(r => r.Message)
            .ToList();
}
