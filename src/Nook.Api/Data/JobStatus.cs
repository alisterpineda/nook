namespace Nook.Api.Data;

/// <summary>Where a job stands. Stored as the member name, so new members need no migration.</summary>
public enum JobStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
}
