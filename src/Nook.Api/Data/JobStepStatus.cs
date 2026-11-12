namespace Nook.Api.Data;

/// <summary>Where a job step stands. Stored as the member name, so new members need no migration.</summary>
public enum JobStepStatus
{
    Running,
    Succeeded,
    Failed,
}
