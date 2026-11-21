using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Nook.Api.Features.Auth;

namespace Nook.Api.Tests.Integration;

/// <summary>
/// Runs the API in memory, with the app's own registrations, against a fresh database and a fresh data folder
/// for one test class. Use it as a class fixture:
/// <c>public class MyTests(NookApiFactory factory) : IClassFixture&lt;NookApiFactory&gt;</c>.
/// </summary>
public sealed class NookApiFactory(PostgresFixture server) : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgresDatabase _database = new(server);

    /// <summary>The connection string of this factory's database.</summary>
    public string ConnectionString => _database.ConnectionString;

    /// <summary>The folder where this factory's API keeps its files. It is deleted when the factory is disposed.</summary>
    public string DataPath { get; } = Path.Combine(Path.GetTempPath(), "nook-tests", Guid.NewGuid().ToString("N"));

    public ValueTask InitializeAsync() => _database.InitializeAsync();

    /// <summary>
    /// The API with single sign-on configured, over this factory's database. The provider is never reached: this is
    /// for what the API does with the settings alone.
    /// </summary>
    public WebApplicationFactory<Program> WithSingleSignOn(bool passwordSignIn = true, string? displayName = null) =>
        WithWebHostBuilder(builder =>
        {
            builder.UseSetting(PasswordSignInOptions.Setting, passwordSignIn ? "true" : "false");
            builder.UseSetting("Nook:Oidc:Authority", "https://sso.test/realms/nook");
            builder.UseSetting("Nook:Oidc:ClientId", "nook");
            builder.UseSetting("Nook:Oidc:ClientSecret", "nook-secret");
            if (displayName is not null)
            {
                builder.UseSetting("Nook:Oidc:DisplayName", displayName);
            }
        });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:nookdb", ConnectionString);
        builder.UseSetting("Nook:DataPath", DataPath);

        // Every test client shares one address, so the real limit would refuse sign-ins once a class has a few tests.
        builder.UseSetting("RateLimiting:Auth:PermitLimit", "100000");
        // The same goes for what a visitor reads and downloads.
        builder.UseSetting("RateLimiting:Anonymous:PermitLimit", "100000");

        // An idle worker looks for jobs this often, so a job no wake announces, such as one a test writes straight
        // to the database, is picked up quickly.
        builder.UseSetting("Nook:Jobs:PollSeconds", "0.2");

        // Keeps what the API logs, so a test can read it.
        builder.ConfigureLogging(logging => logging.AddFakeLogging());
    }

    public override async ValueTask DisposeAsync()
    {
        // The host stops first, so nothing is still writing to the folder.
        await base.DisposeAsync();

        if (Directory.Exists(DataPath))
        {
            Directory.Delete(DataPath, recursive: true);
        }
    }
}
