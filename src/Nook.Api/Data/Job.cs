namespace Nook.Api.Data;

/// <summary>One piece of background work. Its type says how to run it; payload and result are JSON that only the type reads.</summary>
public class Job : Entity
{
    /// <summary>The name of the job type that runs it.</summary>
    public required string Type { get; set; }

    public JobStatus Status { get; set; }

    /// <summary>What the job works on, as JSON.</summary>
    public required string Payload { get; set; }

    /// <summary>What the job produced, as JSON, once it has succeeded.</summary>
    public string? Result { get; set; }

    /// <summary>The job that enqueued this one, if any. A job that still has children cannot be deleted.</summary>
    public Guid? ParentJobId { get; set; }

    /// <summary>The job is not claimed before this time.</summary>
    public DateTimeOffset? NotBefore { get; set; }

    /// <summary>How many runs have ended in success or failure.</summary>
    public int AttemptsMade { get; set; }

    /// <summary>How many runs may end before the job stays failed.</summary>
    public int AttemptsAllowed { get; set; }

    /// <summary>How many times the job was taken back from a worker whose lease expired. Counted apart from attempts.</summary>
    public int ReclaimsMade { get; set; }

    /// <summary>When the running worker's hold on the job runs out, unless renewed.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; set; }

    /// <summary>What went wrong, when the last run failed.</summary>
    public string? Error { get; set; }
}
