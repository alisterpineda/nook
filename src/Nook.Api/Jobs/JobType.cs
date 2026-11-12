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

    /// <summary>Cleans up what the job's payload points at, once the job has been deleted.</summary>
    public virtual Task OnDeletedAsync(string payloadJson, IServiceProvider services) => Task.CompletedTask;
}
