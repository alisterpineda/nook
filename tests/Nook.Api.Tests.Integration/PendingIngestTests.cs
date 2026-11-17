using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Nook.Api.Data;
using Nook.Api.Features.Jobs;
using Nook.Api.Features.Uploads;

namespace Nook.Api.Tests.Integration;

public sealed class PendingIngestTests : IClassFixture<NookApiFactory>, IDisposable
{
    private readonly NookApiFactory _factory;

    // The class's only app. It starts no worker loop, so an uploaded job stays pending.
    private readonly WebApplicationFactory<Program> _app;

    public PendingIngestTests(NookApiFactory factory)
    {
        _factory = factory;
        _app = factory.WithWebHostBuilder(builder => builder.UseSetting("Nook:Jobs:WorkerCount", "0"));
    }

    [Fact]
    public async Task Deleting_a_pending_ingest_removes_its_staged_file()
    {
        var section = await _app.AddSectionAsync();
        using var client = await _app.CreateSignedInClientAsync(BuiltInRole.Admin);
        var upload = await client.UploadAsync(Encoding.UTF8.GetBytes("Any file will do."), "book.epub", section.Id);
        Assert.Equal(HttpStatusCode.Accepted, upload.StatusCode);
        var jobId = (await upload.ReadAsync<UploadResponse>()).JobId;
        Assert.Equal(1, _factory.CountStagedFiles());
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var job = await client.GetAsync<JobDetailResponse>($"/api/jobs/{jobId}");
        Assert.Equal(JobStatus.Pending, job.Status);

        var response = await client.DeleteJobAsync(jobId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var gone = await client.GetAsync($"/api/jobs/{jobId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal(0, _factory.CountStagedFiles());
    }

    public void Dispose() => _app.Dispose();
}
