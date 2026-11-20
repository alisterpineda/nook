using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Features.Jobs;
using Nook.Api.Jobs;

namespace Nook.Api.Features.Activity;

/// <summary>
/// Tells the people watching through the activity hub of each change to a job. Admins hear of every job, and an
/// upload's uploader hears of their own upload.
/// </summary>
public sealed partial class JobChangeNotifier(
    IServiceScopeFactory scopeFactory,
    IHubContext<ActivityHub, IActivityClient> hub,
    ILogger<JobChangeNotifier> logger) : IJobChangeNotifier
{
    public async Task ChangedAsync(Guid jobId)
    {
        try
        {
            // A scope of its own, so the reads never carry what the caller's context has changed.
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            var summary = await db.Jobs
                .AsNoTracking()
                .Where(j => j.Id == jobId)
                .Select(JobsController.ToSummary)
                .SingleOrDefaultAsync();

            // Deleted since: its removal is told on its own.
            if (summary is not null)
            {
                await hub.Clients.Group(ActivityHub.AdminsGroup).JobChanged(summary);
                await TellUploaderAsync(scope, jobId, removed: false);
            }
        }
        catch (Exception exception)
        {
            LogChangeNotTold(logger, exception, jobId);
        }
    }

    public async Task RemovedAsync(Guid jobId)
    {
        try
        {
            await hub.Clients.Group(ActivityHub.AdminsGroup).JobRemoved(jobId);

            await using var scope = scopeFactory.CreateAsyncScope();
            await TellUploaderAsync(scope, jobId, removed: true);
        }
        catch (Exception exception)
        {
            LogRemovalNotTold(logger, exception, jobId);
        }
    }

    /// <summary>
    /// Tells the user who submitted the job where their upload stands. The submitted entry outlives the job, so the
    /// uploader is known after a delete too. A deleted job whose upload has an outcome entry still has a row, which
    /// is sent as a change; one with none is gone from the list, so only its removal is sent.
    /// </summary>
    private async Task TellUploaderAsync(AsyncServiceScope scope, Guid jobId, bool removed)
    {
        var upload = await scope.ServiceProvider.GetRequiredService<UploadActivity>().ForJobAsync(jobId);
        if (upload is not { UserId: { } userId })
        {
            return;
        }

        var uploader = hub.Clients.User(userId.ToString());
        if (upload.Row is { } row)
        {
            await uploader.UploadChanged(row);
        }
        else if (removed)
        {
            await uploader.UploadRemoved(jobId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not tell admins or the uploader that job {JobId} changed.")]
    private static partial void LogChangeNotTold(ILogger logger, Exception exception, Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not tell admins or the uploader that job {JobId} was deleted.")]
    private static partial void LogRemovalNotTold(ILogger logger, Exception exception, Guid jobId);
}
