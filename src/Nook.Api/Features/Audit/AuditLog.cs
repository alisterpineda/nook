using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Jobs;

namespace Nook.Api.Features.Audit;

/// <summary>Who an audit entry says acted, as it was when the entry was written.</summary>
public record AuditActor(Guid? Id, string Name, string Email);

/// <summary>What an audit entry says was acted on.</summary>
public record AuditSubject(string Type, Guid? Id, string Name);

/// <summary>
/// Writes audit entries. An entry is added to the scope's context and not saved, so it is stored by the same save as
/// the change it records.
/// </summary>
public sealed class AuditLog(NookDbContext db, UserManager<NookUser> users)
{
    /// <summary>
    /// Adds an entry for the signed-in user, snapshotting their shown name and email.
    /// </summary>
    /// <param name="isSubmission">Whether the entry is an upload's submitted entry, which carries its own id as its submission id.</param>
    /// <param name="id">An id chosen beforehand for the entry, for a caller that must find it again, or null for a new one.</param>
    public async Task<AuditEntry> AddAsync(
        ClaimsPrincipal actor,
        string action,
        AuditSubject subject,
        object details,
        Guid? submissionId = null,
        bool isSubmission = false,
        Guid? id = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await users.GetUserAsync(actor)
            ?? throw new InvalidOperationException("The audit entry's actor is not a signed-in user.");
        return Add(new AuditActor(user.Id, user.ShownName, user.ShownEmail), action, subject, details, submissionId, isSubmission, id);
    }

    /// <summary>
    /// Adds an entry about an upload for whoever sent it in, read from the upload's submitted entry, which
    /// <paramref name="describe"/> is given to name the subject and details from. Adds nothing and returns null when
    /// there is no submission id or no entry under it.
    /// </summary>
    public async Task<AuditEntry?> AddForSubmissionAsync(
        Guid? submissionId,
        string action,
        Func<AuditEntry, (AuditSubject Subject, object Details)> describe,
        CancellationToken cancellationToken = default)
    {
        if (submissionId is not { } id
            || await db.AuditEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken) is not { } submission)
        {
            return null;
        }

        var (subject, details) = describe(submission);
        return Add(new AuditActor(submission.ActorId, submission.ActorName, submission.ActorEmail), action, subject, details, id);
    }

    /// <summary>Adds an entry for an actor already known, for callers with no signed-in user.</summary>
    public AuditEntry Add(
        AuditActor actor,
        string action,
        AuditSubject subject,
        object details,
        Guid? submissionId = null,
        bool isSubmission = false,
        Guid? id = null)
    {
        var entry = new AuditEntry
        {
            ActorId = actor.Id,
            ActorName = actor.Name,
            ActorEmail = actor.Email,
            Action = action,
            SubjectType = subject.Type,
            SubjectId = subject.Id,
            SubjectName = subject.Name,
            SubmissionId = submissionId,
            Details = JobJson.Serialize(details),
        };
        if (id is { } chosen)
        {
            entry.Id = chosen;
        }

        if (isSubmission)
        {
            entry.SubmissionId = entry.Id;
        }

        db.AuditEntries.Add(entry);
        return entry;
    }
}
