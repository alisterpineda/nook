using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Nook.Api.Features.Audit;

public class AuditLogQuery
{
    [Range(0, int.MaxValue)]
    public int Offset { get; set; }

    [Range(1, 200)]
    public int Limit { get; set; } = 50;

    /// <summary>Only the entries this user acted in. Null means every actor.</summary>
    public Guid? ActorId { get; set; }

    /// <summary>Only the entries of this action name. Null means every action.</summary>
    public string? Action { get; set; }
}

/// <summary>
/// One entry of the audit log. The actor and subject names are as they were when the entry was written. Details is the
/// JSON the entry's action wrote, passed through as it is.
/// </summary>
public record AuditEntryResponse(
    Guid Id,
    DateTimeOffset At,
    Guid? ActorId,
    string ActorName,
    string ActorEmail,
    string Action,
    string SubjectType,
    Guid? SubjectId,
    string SubjectName,
    JsonElement Details);

/// <summary>A user who has acted in the log, with the name and email of their newest entry.</summary>
public record AuditActorResponse(Guid Id, string Name, string Email);

/// <summary>What the log can be narrowed by: every actor with an id, by name, and every action name, in order.</summary>
public record AuditFiltersResponse(IReadOnlyList<AuditActorResponse> Actors, IReadOnlyList<string> Actions);
