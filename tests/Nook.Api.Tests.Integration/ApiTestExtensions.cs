using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nook.Api.Data;
using Nook.Api.Features.Auth;

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

    /// <summary>The fields a validation problem names, as the keys of its <c>errors</c>.</summary>
    public static async Task<List<string>> ErrorFieldsAsync(this HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return [.. body.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name)];
    }

    /// <summary>The name of the session cookie.</summary>
    private const string SessionCookie = "nook";

    /// <summary>Creates a user with <see cref="Password"/>, and a new email unless one is given, and no display name unless one is given.</summary>
    public static Task<NookUser> CreateUserAsync(
        this WebApplicationFactory<Program> factory, BuiltInRole role = BuiltInRole.Contributor, string? email = null, string? displayName = null) =>
        factory.CreateUserAsync(Role.IdOf(role), email, displayName);

    /// <summary>Creates a user as <see cref="CreateUserAsync(WebApplicationFactory{Program}, BuiltInRole, string?, string?)"/> does, in any role, built in or not.</summary>
    public static async Task<NookUser> CreateUserAsync(
        this WebApplicationFactory<Program> factory, Guid roleId, string? email = null, string? displayName = null)
    {
        email ??= $"{Guid.NewGuid():N}@example.test";
        var user = new NookUser { UserName = email, Email = email, RoleId = roleId, DisplayName = displayName };

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<NookUser>>();
        var result = await users.CreateAsync(user, Password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));

        return user;
    }

    /// <summary>A client that holds the session cookie of a new user.</summary>
    public static Task<HttpClient> CreateSignedInClientAsync(
        this WebApplicationFactory<Program> factory, BuiltInRole role = BuiltInRole.Contributor) =>
        factory.CreateSignedInClientAsync(Role.IdOf(role));

    /// <summary>A new user, with a client that holds their session cookie, for a test that needs to know who signed in.</summary>
    public static async Task<(NookUser User, HttpClient Client)> CreateSignedInUserAsync(
        this WebApplicationFactory<Program> factory, BuiltInRole role = BuiltInRole.Contributor, string? displayName = null)
    {
        var user = await factory.CreateUserAsync(role, displayName: displayName);
        var client = factory.CreateClient();
        (await client.LoginAsync(user.Email!, Password)).EnsureSuccessStatusCode();
        return (user, client);
    }

    /// <summary>A client that holds the session cookie of a new user in any role, built in or not.</summary>
    public static async Task<HttpClient> CreateSignedInClientAsync(this WebApplicationFactory<Program> factory, Guid roleId) =>
        (await factory.CreateSignedInUserAsync(roleId)).Client;

    /// <summary>A new user in any role, built in or not, with a client that holds their session cookie.</summary>
    public static async Task<(NookUser User, HttpClient Client)> CreateSignedInUserAsync(
        this WebApplicationFactory<Program> factory, Guid roleId)
    {
        var user = await factory.CreateUserAsync(roleId);
        var client = factory.CreateClient();
        (await client.LoginAsync(user.Email!, Password)).EnsureSuccessStatusCode();
        return (user, client);
    }

    /// <summary>
    /// A new user who joined through single sign-on and has no password, with a client that holds their session cookie.
    /// The cookie keeps working because sessions are checked only every few minutes, so this is not for a factory from
    /// <see cref="WithSessionsCheckedOnEveryRequest"/>.
    /// </summary>
    public static async Task<(NookUser User, HttpClient Client)> CreateSignedInUserWithoutPasswordAsync(
        this WebApplicationFactory<Program> factory, BuiltInRole role = BuiltInRole.Contributor, string? displayName = null)
    {
        var (user, client) = await factory.CreateSignedInUserAsync(role, displayName);

        await factory.AddSingleSignOnLoginAsync(user.Id);

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<NookUser>>();
        var stored = await users.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(stored);
        var removed = await users.RemovePasswordAsync(stored);
        Assert.True(removed.Succeeded, string.Join("; ", removed.Errors.Select(e => e.Description)));

        return (user, client);
    }

    /// <summary>Gives a user a login made through single sign-on, as signing in at the provider would.</summary>
    public static async Task AddSingleSignOnLoginAsync(this WebApplicationFactory<Program> factory, Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<NookUser>>();
        var stored = await users.FindByIdAsync(userId.ToString());
        Assert.NotNull(stored);
        var added = await users.AddLoginAsync(stored, new UserLoginInfo(OidcSignIn.Scheme, Guid.NewGuid().ToString("N"), OidcSignIn.Scheme));
        Assert.True(added.Succeeded, string.Join("; ", added.Errors.Select(e => e.Description)));
    }

    /// <summary>
    /// Eight new signed-in admins each send a request about the next one, and the last about the first, all at once.
    /// Gives the answers in the admins' order.
    /// </summary>
    public static async Task<HttpResponseMessage[]> AdminsActOnEachOtherAtOnceAsync(
        this WebApplicationFactory<Program> factory, Func<HttpClient, Guid, Task<HttpResponseMessage>> request)
    {
        var admins = new List<(NookUser User, HttpClient Client)>();
        for (var i = 0; i < 8; i++)
        {
            admins.Add(await factory.CreateSignedInUserAsync(BuiltInRole.Admin));
        }

        try
        {
            return await Task.WhenAll(admins.Select((admin, i) => request(admin.Client, admins[(i + 1) % admins.Count].User.Id)));
        }
        finally
        {
            admins.ForEach(a => a.Client.Dispose());
        }
    }

    /// <summary>The API with every request's session checked against the user's row, so a change to the user reaches their cookie at once.</summary>
    public static WebApplicationFactory<Program> WithSessionsCheckedOnEveryRequest(this WebApplicationFactory<Program> factory) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero)));

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

    /// <summary>The whole <c>Set-Cookie</c> value of the session cookie a response sets, with its attributes.</summary>
    public static string SessionSetCookie(this HttpResponseMessage response) =>
        response.SetCookies().Single(IsSessionCookie);

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

    /// <summary>A role that is not saved yet, with a name of its own, that sees every section unless told otherwise.</summary>
    public static Role NewRole(
        string name, bool canUpload, bool allSections = true, ContentAccess defaultArea = ContentAccess.Download) => new()
    {
        Name = $"{name} {Guid.NewGuid():N}",
        ContentAccess = ContentAccess.Download,
        DefaultAreaAccess = defaultArea,
        AllSections = allSections,
        CanUpload = canUpload,
    };

    /// <summary>The messages the API has logged at a level so far, oldest first.</summary>
    public static IReadOnlyList<string> ReadLogs(this WebApplicationFactory<Program> factory, LogLevel level) =>
        factory.Services.GetFakeLogCollector().GetSnapshot()
            .Where(r => r.Level == level)
            .Select(r => r.Message)
            .ToList();
}
