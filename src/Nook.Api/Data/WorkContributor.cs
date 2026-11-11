namespace Nook.Api.Data;

/// <summary>A person's part in a work, with the place they are shown in among the work's contributors.</summary>
public class WorkContributor : Entity
{
    public Guid WorkId { get; set; }

    public Guid PersonId { get; set; }

    /// <summary>What the person did on the work. Author unless set otherwise.</summary>
    public ContributorRole Role { get; set; } = ContributorRole.Author;

    /// <summary>Where the person is shown among the work's contributors, lowest first.</summary>
    public int Order { get; set; }
}
