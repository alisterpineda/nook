using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features;
using Nook.Api.Features.Jobs;
using Nook.Api.Jobs;

namespace Nook.Api.Tests.Integration;

public sealed class JobApiTests : IClassFixture<NookApiFactory>, IDisposable
{
    // Far enough off that a job enqueued not before it stays pending, as enqueued or seeded, while the worker runs.
    private static readonly DateTimeOffset Never = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // The class runs jobs only on this app, so no second app's workers share its database.
    private readonly WebApplicationFactory<Program> _app;

    public JobApiTests(NookApiFactory factory)
    {
        _app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddTestJobs()));
    }

    [Fact]
    public async Task Job_enqueued_with_a_parent_is_listed_among_its_children()
    {
        var parent = await EnqueueAsync("parent", notBefore: Never);
        var child = await EnqueueAsync("child", parent, Never);
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);

        var detail = await client.GetAsync<JobDetailResponse>($"/api/jobs/{parent}");

        Assert.Equal(parent, detail.Id);
        Assert.Equal(RecordingJob.TypeName, detail.Type);
        Assert.Equal(JobStatus.Pending, detail.Status);
        Assert.Null(detail.ParentJobId);
        var listed = Assert.Single(detail.Children);
        Assert.Equal(child, listed.Id);
        Assert.Equal(parent, listed.ParentJobId);
        Assert.Equal(RecordingJob.TypeName, listed.Type);

        var childDetail = await client.GetAsync<JobDetailResponse>($"/api/jobs/{child}");
        Assert.Equal(parent, childDetail.ParentJobId);
        Assert.Empty(childDetail.Children);
    }

    [Fact]
    public async Task Jobs_list_comes_newest_first_in_pages()
    {
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);
        var before = (await client.GetAsync<Page<JobSummaryResponse>>("/api/jobs?limit=1")).Total;
        var first = await EnqueueAsync("first");
        var second = await EnqueueAsync("second");
        var third = await EnqueueAsync("third");

        var top = await client.GetAsync<Page<JobSummaryResponse>>("/api/jobs?limit=2");
        var next = await client.GetAsync<Page<JobSummaryResponse>>("/api/jobs?limit=1&offset=2");

        Assert.Equal([third, second], top.Items.Select(j => j.Id));
        Assert.Equal((before + 3, 0, 2), (top.Total, top.Offset, top.Limit));
        Assert.Equal([first], next.Items.Select(j => j.Id));
        Assert.Equal((before + 3, 2, 1), (next.Total, next.Offset, next.Limit));

        var defaults = await client.GetAsync<Page<JobSummaryResponse>>("/api/jobs");
        Assert.Equal(50, defaults.Limit);
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=201")]
    [InlineData("offset=-1")]
    public async Task Jobs_list_rejects_a_bad_query(string query)
    {
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);

        var response = await client.GetAsync($"/api/jobs?{query}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Job_detail_shows_its_counts_result_and_steps_in_order()
    {
        var id = await EnqueueAsync("detail", notBefore: Never);
        var startedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var job = await db.Jobs.SingleAsync(j => j.Id == id, TestContext.Current.CancellationToken);
            job.Status = JobStatus.Succeeded;
            job.Result = """{"outcome":"done","count":2}""";
            job.AttemptsMade = 2;
            db.JobSteps.AddRange(
                new JobStep { JobId = id, Name = "second", Order = 1, Attempt = 2, Status = JobStepStatus.Succeeded, StartedAt = startedAt.AddMinutes(3), FinishedAt = startedAt.AddMinutes(4) },
                new JobStep { JobId = id, Name = "first", Order = 0, Attempt = 2, Status = JobStepStatus.Succeeded, StartedAt = startedAt.AddMinutes(2), FinishedAt = startedAt.AddMinutes(3) },
                new JobStep { JobId = id, Name = "first", Order = 0, Attempt = 1, Status = JobStepStatus.Failed, Error = "boom", StartedAt = startedAt, FinishedAt = startedAt.AddMinutes(1) });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);

        var detail = await client.GetAsync<JobDetailResponse>($"/api/jobs/{id}");

        Assert.Equal(JobStatus.Succeeded, detail.Status);
        Assert.Null(detail.Error);
        Assert.Equal((2, 1, 0), (detail.AttemptsMade, detail.AttemptsAllowed, detail.ReclaimsMade));
        Assert.Equal(Never, detail.NotBefore);
        Assert.NotEqual(default, detail.CreatedAt);
        Assert.NotNull(detail.Result);
        Assert.Equal("done", detail.Result.Value.GetProperty("outcome").GetString());
        Assert.Equal(2, detail.Result.Value.GetProperty("count").GetInt32());
        Assert.Equal(
            [
                new JobStepResponse("first", 0, 1, JobStepStatus.Failed, "boom", startedAt, startedAt.AddMinutes(1)),
                new JobStepResponse("first", 0, 2, JobStepStatus.Succeeded, null, startedAt.AddMinutes(2), startedAt.AddMinutes(3)),
                new JobStepResponse("second", 1, 2, JobStepStatus.Succeeded, null, startedAt.AddMinutes(3), startedAt.AddMinutes(4)),
            ],
            detail.Steps);
    }

    [Fact]
    public async Task Job_enqueued_with_a_not_before_time_shows_it()
    {
        var id = await EnqueueAsync("later", notBefore: Never);
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);

        var detail = await client.GetAsync<JobDetailResponse>($"/api/jobs/{id}");

        Assert.Equal(Never, detail.NotBefore);
        Assert.Null(detail.Result);
        Assert.Empty(detail.Steps);
    }

    [Fact]
    public async Task Job_of_an_unregistered_type_is_enqueued_with_one_attempt()
    {
        var id = await _app.EnqueueJobAsync("no-such-type", new { }, notBefore: Never);
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);

        var detail = await client.GetAsync<JobDetailResponse>($"/api/jobs/{id}");

        Assert.Equal("no-such-type", detail.Type);
        Assert.Equal(JobStatus.Pending, detail.Status);
        Assert.Equal(1, detail.AttemptsAllowed);
    }

    [Fact]
    public async Task Unknown_job_is_not_found()
    {
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);

        var response = await client.GetAsync($"/api/jobs/{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(UserRole.Member, false)]
    [InlineData(UserRole.Member, true)]
    [InlineData(UserRole.Guest, false)]
    [InlineData(UserRole.Guest, true)]
    public async Task Member_or_guest_is_forbidden_from_reading_jobs(UserRole role, bool oneJob)
    {
        var id = await EnqueueAsync("hidden");
        using var client = await _app.CreateSignedInClientAsync(role);

        var response = await client.GetAsync(oneJob ? $"/api/jobs/{id}" : "/api/jobs", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Job_detail_does_not_show_the_payload()
    {
        var secret = $"/srv/nook/staging/{Guid.NewGuid():N}";
        var id = await EnqueueAsync(secret);
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);

        var detail = await client.GetStringAsync($"/api/jobs/{id}", TestContext.Current.CancellationToken);
        var list = await client.GetStringAsync("/api/jobs?limit=200", TestContext.Current.CancellationToken);

        Assert.Contains(id.ToString(), detail);
        Assert.DoesNotContain(secret, detail);
        Assert.DoesNotContain("payload", detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(id.ToString(), list);
        Assert.DoesNotContain(secret, list);
    }

    [Fact]
    public async Task Retrying_a_failed_job_runs_it_again_from_its_first_step()
    {
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);
        var id = await EnqueueAsync(Guid.NewGuid().ToString(), type: FlakyJob.TypeName);
        var failed = await client.WaitForJobAsync(id);
        Assert.Equal(JobStatus.Failed, failed.Status);

        // Several polls' worth, so a worker that took a failed job back up would have done it by now.
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var stillFailed = await client.GetAsync<JobDetailResponse>($"/api/jobs/{id}");
        Assert.Equal(JobStatus.Failed, stillFailed.Status);
        Assert.Equal(FlakyJob.ErrorMessage, stillFailed.Error);
        Assert.Equal((1, 1), (stillFailed.AttemptsMade, stillFailed.AttemptsAllowed));

        var response = await client.RetryJobAsync(id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var job = await client.WaitForJobAsync(id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Null(job.Error);
        Assert.Equal((2, 2, 0), (job.AttemptsMade, job.AttemptsAllowed, job.ReclaimsMade));
        Assert.Equal(
            [
                ("prepare", 1, JobStepStatus.Succeeded, (string?)null),
                ("flake", 1, JobStepStatus.Failed, FlakyJob.ErrorMessage),
                ("prepare", 2, JobStepStatus.Succeeded, null),
                ("flake", 2, JobStepStatus.Succeeded, null),
            ],
            job.Steps.Select(s => (s.Name, s.Attempt, s.Status, s.Error)));
    }

    [Theory]
    [InlineData(JobStatus.Pending)]
    [InlineData(JobStatus.Succeeded)]
    public async Task Retrying_a_job_that_is_not_failed_is_refused(JobStatus status)
    {
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);
        var id = await EnqueueAsync("not failed", notBefore: status == JobStatus.Pending ? Never : null);
        var before = status == JobStatus.Pending
            ? await client.GetAsync<JobDetailResponse>($"/api/jobs/{id}")
            : await client.WaitForJobAsync(id);
        Assert.Equal(status, before.Status);

        var response = await client.RetryJobAsync(id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var after = await client.GetAsync<JobDetailResponse>($"/api/jobs/{id}");
        Assert.Equal(
            (before.Status, before.NotBefore, before.AttemptsMade, before.AttemptsAllowed, before.Steps.Count),
            (after.Status, after.NotBefore, after.AttemptsMade, after.AttemptsAllowed, after.Steps.Count));
    }

    [Fact]
    public async Task Retrying_an_unknown_job_is_not_found()
    {
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);

        var response = await client.RetryJobAsync(Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(UserRole.Member)]
    [InlineData(UserRole.Guest)]
    public async Task Member_or_guest_is_forbidden_from_retrying(UserRole role)
    {
        using var admin = await _app.CreateSignedInClientAsync(UserRole.Admin);
        var id = await EnqueueAsync(Guid.NewGuid().ToString(), type: FailingJob.TypeName);
        Assert.Equal(JobStatus.Failed, (await admin.WaitForJobAsync(id)).Status);
        using var client = await _app.CreateSignedInClientAsync(role);

        var response = await client.RetryJobAsync(id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var after = await admin.GetAsync<JobDetailResponse>($"/api/jobs/{id}");
        Assert.Equal((JobStatus.Failed, 1), (after.Status, after.AttemptsMade));
    }

    [Fact]
    public async Task Deleting_a_finished_job_removes_it_and_its_steps_and_runs_its_cleanup()
    {
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);
        var note = Guid.NewGuid().ToString();
        var id = await EnqueueAsync(note);
        var finished = await client.WaitForJobAsync(id);
        Assert.Equal(JobStatus.Succeeded, finished.Status);
        Assert.Equal(2, await CountStepsAsync(id));
        Assert.DoesNotContain(note, Recorder.Cleanups);

        var response = await client.DeleteJobAsync(id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusCodeAsync(client, id));
        Assert.Equal(0, await CountStepsAsync(id));
        Assert.Equal(1, Recorder.Cleanups.Count(n => n == note));
    }

    [Fact]
    public async Task Deleting_a_pending_job_removes_it()
    {
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);
        var note = Guid.NewGuid().ToString();
        var id = await EnqueueAsync(note, notBefore: Never);

        var response = await client.DeleteJobAsync(id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusCodeAsync(client, id));
        Assert.Contains(note, Recorder.Cleanups);
        Assert.DoesNotContain(note, Recorder.Notes);
    }

    [Fact]
    public async Task Deleting_a_job_of_an_unregistered_type_removes_it()
    {
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);
        var id = await EnqueueAsync(Guid.NewGuid().ToString(), notBefore: Never, type: "no-such-type");

        var response = await client.DeleteJobAsync(id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusCodeAsync(client, id));
    }

    [Fact]
    public async Task Deleting_a_running_job_is_refused()
    {
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);
        var counter = _app.Services.GetRequiredService<JobCounter>();
        var key = Guid.NewGuid();
        counter.Hold(key);
        try
        {
            var id = await _app.EnqueueJobAsync(CountingJob.TypeName, new CountingPayload(key));
            await counter.WaitUntilStartedAsync(key);

            var response = await client.DeleteJobAsync(id);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var after = await client.GetAsync<JobDetailResponse>($"/api/jobs/{id}");
            Assert.Equal(JobStatus.Running, after.Status);
            Assert.Single(after.Steps);

            counter.Release(key);
            Assert.Equal(JobStatus.Succeeded, (await client.WaitForJobAsync(id)).Status);
        }
        finally
        {
            counter.Release(key);
        }
    }

    [Fact]
    public async Task Deleting_a_job_with_children_is_refused()
    {
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);
        var note = Guid.NewGuid().ToString();
        var parent = await EnqueueAsync(note, notBefore: Never);
        var child = await EnqueueAsync(Guid.NewGuid().ToString(), parent, Never);

        var response = await client.DeleteJobAsync(parent);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var after = await client.GetAsync<JobDetailResponse>($"/api/jobs/{parent}");
        Assert.Equal([child], after.Children.Select(c => c.Id));
        Assert.DoesNotContain(note, Recorder.Cleanups);
    }

    [Fact]
    public async Task Deleting_an_unknown_job_is_not_found()
    {
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);

        var response = await client.DeleteJobAsync(Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(UserRole.Member)]
    [InlineData(UserRole.Guest)]
    public async Task Member_or_guest_is_forbidden_from_deleting(UserRole role)
    {
        var note = Guid.NewGuid().ToString();
        var id = await EnqueueAsync(note, notBefore: Never);
        using var client = await _app.CreateSignedInClientAsync(role);

        var response = await client.DeleteJobAsync(id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var admin = await _app.CreateSignedInClientAsync(UserRole.Admin);
        Assert.Equal(HttpStatusCode.OK, await GetStatusCodeAsync(admin, id));
        Assert.DoesNotContain(note, Recorder.Cleanups);
    }

    public void Dispose() => _app.Dispose();

    private JobRecorder Recorder => _app.Services.GetRequiredService<JobRecorder>();

    private static async Task<HttpStatusCode> GetStatusCodeAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"/api/jobs/{id}", TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private async Task<int> CountStepsAsync(Guid id)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        return await db.JobSteps.CountAsync(s => s.JobId == id, TestContext.Current.CancellationToken);
    }

    private Task<Guid> EnqueueAsync(
        string note, Guid? parentJobId = null, DateTimeOffset? notBefore = null, string type = RecordingJob.TypeName) =>
        _app.EnqueueJobAsync(type, new RecordingPayload(note), parentJobId, notBefore);
}
