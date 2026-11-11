using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nook.Api.Tests.Integration;

/// <summary>
/// Runs the API in memory, with the app's own registrations, against a fresh database for one test class.
/// Use it as a class fixture: <c>public class MyTests(NookApiFactory factory) : IClassFixture&lt;NookApiFactory&gt;</c>.
/// </summary>
public sealed class NookApiFactory(PostgresFixture server) : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgresDatabase _database = new(server);

    /// <summary>The connection string of this factory's database.</summary>
    public string ConnectionString => _database.ConnectionString;

    public ValueTask InitializeAsync() => _database.InitializeAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:nookdb", ConnectionString);

        // Every test client shares one address, so the real limit would refuse sign-ins once a class has a few tests.
        builder.UseSetting("RateLimiting:Auth:PermitLimit", "100000");
    }
}
