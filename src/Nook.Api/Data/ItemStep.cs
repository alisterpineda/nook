namespace Nook.Api.Data;

/// <summary>One named processing step for an item. An item holds each step name once.</summary>
public class ItemStep : Entity
{
    public Guid ItemId { get; set; }

    /// <summary>The step's name, such as "extract-metadata".</summary>
    public required string StepName { get; set; }

    /// <summary>The version of the step that ran, so a newer version can be told apart.</summary>
    public int StepVersion { get; set; }

    public ItemStepStatus Status { get; set; }

    /// <summary>What went wrong, when the step failed.</summary>
    public string? Error { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}
