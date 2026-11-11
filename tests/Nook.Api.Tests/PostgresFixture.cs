using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Nook.Api.Tests.PostgresFixture))]

namespace Nook.Api.Tests;

/// <summary>
/// Starts one PostgreSQL container for the whole test run.
/// Test classes get their own database from it through <see cref="PostgresDatabase"/>.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates a new, empty database and returns a connection string for it.</summary>
    public async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var name = $"test_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }
}

/// <summary>
/// A fresh, empty database for one test class. Use it as a class fixture:
/// <c>public class MyTests(PostgresDatabase database) : IClassFixture&lt;PostgresDatabase&gt;</c>.
/// </summary>
public sealed class PostgresDatabase(PostgresFixture server) : IAsyncLifetime
{
    private string? _connectionString;

    public string ConnectionString =>
        _connectionString ?? throw new InvalidOperationException("The database has not been created yet.");

    public async ValueTask InitializeAsync() => _connectionString = await server.CreateDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
