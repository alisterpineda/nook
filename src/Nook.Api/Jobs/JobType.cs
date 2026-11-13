namespace Nook.Api.Jobs;

/// <summary>
/// A kind of background job. It names itself, declares its steps in code, and alone reads the JSON payload
/// its jobs carry. Register one with <see cref="JobServiceExtensions.AddJobType{T}"/>; it lives as a singleton.
/// </summary>
public abstract class JobType
{
    /// <summary>The name jobs of this type are stored under. Changing it orphans the jobs already stored.</summary>
    public abstract string Name { get; }

    /// <summary>
    /// How many runs a job of this type may make before it stays failed. A run that fails starts again from
    /// the first step, so every step must be safe to repeat.
    /// </summary>
    public virtual int Attempts => 1;

    /// <summary>
    /// Builds one run of a job from its payload. <paramref name="services"/> belong to that run alone, so
    /// scoped services, such as the database context, are taken from them.
    /// </summary>
    public abstract JobRun CreateRun(string payloadJson, IServiceProvider services);

    /// <summary>
    /// Runs once a job of this type is recorded as succeeded, in a scope of its own, for work that must wait until
    /// the job can no longer run again, such as removing an input the job kept while it might. The job reads as
    /// succeeded a moment before this runs, and this runs at most once: an exception from it is logged and changes
    /// nothing on the job, and a process that stops in between never runs it.
    /// </summary>
    public virtual Task OnSucceededAsync(string payloadJson, IServiceProvider services) => Task.CompletedTask;

    /// <summary>Cleans up what the job's payload points at, once the job has been deleted.</summary>
    public virtual Task OnDeletedAsync(string payloadJson, IServiceProvider services) => Task.CompletedTask;
}
