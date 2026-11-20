namespace Nook.Api.Jobs;

/// <summary>
/// Hears of each change to a job, once the write that made it has committed, so something outside the job system can
/// tell whoever watches. An implementation never throws: a failure to tell is its own to log, and never fails or
/// undoes the change it reports.
/// </summary>
public interface IJobChangeNotifier
{
    /// <summary>A job was added, or its status changed.</summary>
    Task ChangedAsync(Guid jobId);

    /// <summary>A job was deleted.</summary>
    Task RemovedAsync(Guid jobId);
}

/// <summary>The notifier a host gets when it registers none: nobody is told.</summary>
public sealed class NoJobChangeNotifier : IJobChangeNotifier
{
    public Task ChangedAsync(Guid jobId) => Task.CompletedTask;

    public Task RemovedAsync(Guid jobId) => Task.CompletedTask;
}
