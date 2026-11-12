namespace Nook.Api.Data;

/// <summary>One step of one run of a job.</summary>
public class JobStep : Entity
{
    public Guid JobId { get; set; }

    /// <summary>The step's name, as its job type declares it.</summary>
    public required string Name { get; set; }

    /// <summary>Where the step comes among its run's steps, lowest first.</summary>
    public int Order { get; set; }

    /// <summary>The number of the run the step belongs to, starting at 1.</summary>
    public int Attempt { get; set; }

    public JobStepStatus Status { get; set; }

    /// <summary>What went wrong, when the step failed.</summary>
    public string? Error { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}
