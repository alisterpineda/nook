namespace Nook.Api.Jobs;

/// <summary>One named step of a run. The token is cancelled when the application stops.</summary>
public sealed record JobRunStep(string Name, Func<CancellationToken, Task> RunAsync);

/// <summary>
/// One run of a job: its steps in order, and what it produced. A step that closes over the run can set
/// the result, or complete the job so the steps after it are skipped.
/// </summary>
public sealed class JobRun
{
    private readonly List<JobRunStep> _steps = [];

    /// <summary>The steps, in the order they run.</summary>
    public IReadOnlyList<JobRunStep> Steps => _steps;

    /// <summary>The run's result as JSON, or null when it produced none.</summary>
    public string? ResultJson { get; private set; }

    /// <summary>Whether a step has completed the job, so no later step runs.</summary>
    public bool IsCompleted { get; private set; }

    /// <summary>Adds a step after those already added.</summary>
    public JobRun AddStep(string name, Func<CancellationToken, Task> runAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(runAsync);
        if (_steps.Any(s => s.Name == name))
        {
            throw new ArgumentException($"The run already has a step named '{name}'.", nameof(name));
        }

        _steps.Add(new JobRunStep(name, runAsync));
        return this;
    }

    /// <summary>Sets what the job produced. It is stored when the run succeeds.</summary>
    public void SetResult<T>(T result) => ResultJson = JobJson.Serialize(result);

    /// <summary>Ends the job successfully with this result once the current step returns; the steps after it do not run.</summary>
    public void Complete<T>(T result)
    {
        SetResult(result);
        IsCompleted = true;
    }
}
