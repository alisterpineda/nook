namespace Nook.Api.Jobs;

/// <summary>Wakes idle workers in this process when there may be a job for them, so they need not wait for their next poll.</summary>
public sealed class JobSignal
{
    private TaskCompletionSource _next = NewSource();

    /// <summary>
    /// A task that completes at the next <see cref="Wake"/>. Take it before looking for a job, so a wake that
    /// comes while looking is not missed.
    /// </summary>
    public Task NextWakeAsync() => Volatile.Read(ref _next).Task;

    /// <summary>Wakes every worker waiting on the current task.</summary>
    public void Wake() => Interlocked.Exchange(ref _next, NewSource()).TrySetResult();

    private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
