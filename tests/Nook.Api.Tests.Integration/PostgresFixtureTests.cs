using Npgsql;

namespace Nook.Api.Tests.Integration;

public class PostgresFixtureTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    [Fact]
    public async Task Fresh_database_accepts_queries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT 1", connection);

        var result = await command.ExecuteScalarAsync(cancellationToken);

        Assert.Equal(1, result);
    }
}
