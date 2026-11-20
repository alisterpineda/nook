namespace Nook.Api.Data;

/// <summary>
/// One line of the audit log: who did what to which subject. It keeps a snapshot of the names and has no foreign
/// keys, so it stays as written after the user or the subject it names is deleted.
/// </summary>
public class AuditEntry : Entity
{
    /// <summary>The id of the user who acted, or null when no user did.</summary>
    public Guid? ActorId { get; set; }

    /// <summary>The actor's name when the entry was written.</summary>
    public required string ActorName { get; set; }

    /// <summary>The actor's email when the entry was written.</summary>
    public required string ActorEmail { get; set; }

    /// <summary>What happened, as a name such as <c>upload.submitted</c>.</summary>
    public required string Action { get; set; }

    /// <summary>The kind of thing acted on.</summary>
    public required string SubjectType { get; set; }

    /// <summary>The id of the thing acted on, if it has one.</summary>
    public Guid? SubjectId { get; set; }

    /// <summary>The subject's name when the entry was written.</summary>
    public required string SubjectName { get; set; }

    /// <summary>The upload this entry belongs to, if it is about one. Found by this column, never through <see cref="Details"/>.</summary>
    public Guid? SubmissionId { get; set; }

    /// <summary>What else is worth knowing about the action, as JSON that only its reader parses.</summary>
    public required string Details { get; set; }
}
