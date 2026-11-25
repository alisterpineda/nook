using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Nook.Api.Features.Build;
using Npgsql;

namespace Nook.Api.Tests.Integration;

public partial class StartupMigrationTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Starting_the_api_migrates_an_empty_database()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await StartApiAsync(cancellationToken);

        var userTables = await QueryAsync(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'asp_net_users'",
            cancellationToken);
        var migrations = await QueryAsync("SELECT migration_id FROM \"__EFMigrationsHistory\"", cancellationToken);

        Assert.Single(userTables);
        Assert.Contains(migrations, id => id.EndsWith("_InitialCreate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Starting_the_api_logs_its_version()
    {
        await StartApiAsync(TestContext.Current.CancellationToken);

        Assert.Single(factory.ReadLogs(LogLevel.Information), $"Nook {BuildVersion.Current} is starting.");
    }

    [Fact]
    public async Task All_table_and_column_names_are_snake_case()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await StartApiAsync(cancellationToken);

        var tables = await QueryAsync(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory'",
            cancellationToken);
        var columns = await QueryAsync(
            "SELECT table_name || '.' || column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory'",
            cancellationToken);

        Assert.NotEmpty(tables);
        Assert.All(tables, name => Assert.Matches(SnakeCase(), name));
        Assert.All(columns, name => Assert.Matches(SnakeCase(), name.Split('.')[1]));
    }

    private async Task StartApiAsync(CancellationToken cancellationToken)
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/setup", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<List<string>> QueryAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var values = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex SnakeCase();
}
