using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Jobs;
using Npgsql;

namespace Nook.Api.Tests.Integration;

public sealed class JobShutdownTests : IClassFixture<NookApiFactory>, IDisposable
{
    private readonly NookApiFactory _factory;

    // The class runs jobs only on this app, so no second app's workers share its database.
    private readonly WebApplicationFactory<Program> _app;

    public JobShutdownTests(NookApiFactory factory)
    {
        _factory = factory;
        _app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddTestJobs()));
    }

    [Fact]
    public async Task Job_running_at_shutdown_is_pending_afterwards()
    {
        var counter = _app.Services.GetRequiredService<JobCounter>();
        var key = Guid.NewGuid();
        counter.Hold(key);

        var id = await EnqueueAsync(new CountingPayload(key));
        await counter.WaitUntilStartedAsync(key);

        // Stopping the host cancels the worker and waits for it, as a clean shutdown of the application does.
        await _app.DisposeAsync();

        var job = await ReadJobAsync(id);
        Assert.Equal(("Pending", 0, 0, (DateTimeOffset?)null), job);
        Assert.Equal([("Failed", "The application shut down.")], await ReadStepsAsync(id));
    }

    public void Dispose() => _app.Dispose();

    private Task<Guid> EnqueueAsync(object payload) => _app.EnqueueJobAsync(CountingJob.TypeName, payload);

    // Read straight from the database, since the app that would serve the job is gone.
    private async Task<(string Status, int ReclaimsMade, int AttemptsMade, DateTimeOffset? LeaseExpiresAt)> ReadJobAsync(Guid id)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(_factory.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT status, reclaims_made, attempts_made, lease_expires_at FROM jobs WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken), $"Job {id} is not in the database.");
        return (
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3));
    }

    private async Task<List<(string Status, string? Error)>> ReadStepsAsync(Guid jobId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(_factory.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT status, error FROM job_steps WHERE job_id = @id ORDER BY attempt, \"order\"",
            connection);
        command.Parameters.AddWithValue("id", jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var steps = new List<(string, string?)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            steps.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        return steps;
    }
}
