using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nook.Api.Data;
using Nook.Api.Features;
using Nook.Api.Features.Jobs;
using Nook.Api.Features.Uploads;

namespace Nook.Api.Tests.Integration;

public sealed class UploadTests : IClassFixture<NookApiFactory>, IDisposable
{
    // Small, so a test can go over the limit without sending megabytes.
    private const int MaxBytes = 1024;

    private readonly NookApiFactory _factory;

    // The class runs jobs only on this app, so no second app's workers share its database.
    private readonly WebApplicationFactory<Program> _app;

    public UploadTests(NookApiFactory factory)
    {
        _factory = factory;
        _app = factory.WithWebHostBuilder(builder => builder.UseSetting("Nook:Uploads:MaxBytes", MaxBytes.ToString()));
    }

    [Fact]
    public async Task Upload_of_a_file_that_is_not_an_epub_returns_a_job_that_fails_as_an_unsupported_format()
    {
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);

        var response = await client.UploadAsync(TestData.NotABook, "notes.epub", Library.DefaultId);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var upload = await response.ReadAsync<UploadResponse>();
        var job = await client.WaitForJobAsync(upload.JobId);
        Assert.Equal("ingest", job.Type);
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("unsupported format", job.Error);
        var step = Assert.Single(job.Steps);
        Assert.Equal("identify-format", step.Name);
        Assert.Equal(JobStepStatus.Failed, step.Status);
        Assert.Contains("unsupported format", step.Error);
    }

    [Fact]
    public async Task Upload_without_a_library_id_is_a_validation_error()
    {
        await AssertRefusedAsync(client => client.UploadAsync(TestData.NotABook, "notes.txt", libraryId: null));
    }

    [Fact]
    public async Task Upload_with_an_unknown_library_id_is_a_validation_error()
    {
        await AssertRefusedAsync(client => client.UploadAsync(TestData.NotABook, "notes.txt", Guid.CreateVersion7()));
    }

    [Fact]
    public async Task Upload_without_a_file_is_a_validation_error()
    {
        await AssertRefusedAsync(client => client.UploadAsync(bytes: null, "notes.txt", Library.DefaultId));
    }

    [Theory]
    [InlineData(UserRole.Member)]
    [InlineData(UserRole.Guest)]
    public async Task Member_or_guest_is_forbidden_from_uploading(UserRole role)
    {
        using var admin = await _app.CreateSignedInClientAsync(UserRole.Admin);
        var jobsBefore = await admin.CountJobsAsync();
        var stagedBefore = _factory.CountStagedFiles();
        using var client = await _app.CreateSignedInClientAsync(role);

        var response = await client.UploadAsync(TestData.NotABook, "notes.txt", Library.DefaultId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(jobsBefore, await admin.CountJobsAsync());
        Assert.Equal(stagedBefore, _factory.CountStagedFiles());
    }

    [Fact]
    public void Upload_limit_defaults_to_200_megabytes()
    {
        // No worker loop, so this app runs no jobs beside the class's own.
        using var app = _factory.WithWebHostBuilder(builder => builder.UseSetting("Nook:Jobs:WorkerCount", "0"));

        var options = app.Services.GetRequiredService<IOptions<UploadOptions>>().Value;

        Assert.Equal(200 * 1024 * 1024, options.MaxBytes);
    }

    [Fact]
    public async Task Upload_just_over_the_limit_is_refused()
    {
        await AssertRefusedAsync(
            client => client.UploadAsync(new byte[5_000], "big.epub", Library.DefaultId),
            HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Upload_far_over_the_limit_is_refused()
    {
        await AssertRefusedAsync(
            client => client.UploadAsync(new byte[3 * 1024 * 1024], "huge.epub", Library.DefaultId),
            HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Upload_within_the_limit_is_accepted()
    {
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);

        var response = await client.UploadAsync(new byte[500], "small.epub", Library.DefaultId);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    public void Dispose() => _app.Dispose();

    /// <summary>
    /// Sends the upload as an admin and checks it is refused with problem details, by default as a validation error,
    /// and that it created no job and staged no file.
    /// </summary>
    private async Task AssertRefusedAsync(
        Func<HttpClient, Task<HttpResponseMessage>> upload, HttpStatusCode status = HttpStatusCode.BadRequest)
    {
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);
        var jobsBefore = await client.CountJobsAsync();
        var stagedBefore = _factory.CountStagedFiles();

        var response = await upload(client);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(jobsBefore, await client.CountJobsAsync());
        Assert.Equal(stagedBefore, _factory.CountStagedFiles());
    }
}
