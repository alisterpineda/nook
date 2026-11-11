namespace Nook.Api.Data;

/// <summary>Where an item step stands. Stored as the member name, so new members need no migration.</summary>
public enum ItemStepStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
}
