using Nook.Api.Data;

namespace Nook.Api.Features.Audit;

/// <summary>The names of what the audit log records, as written in <see cref="AuditEntry.Action"/>.</summary>
public static class AuditActions
{
    public const string UploadSubmitted = "upload.submitted";
    public const string UploadAdded = "upload.added";
    public const string UploadDuplicate = "upload.duplicate";
    public const string UploadFailed = "upload.failed";
    public const string JobRetried = "job.retried";
    public const string JobDeleted = "job.deleted";
    public const string WorkEdited = "work.edited";
    public const string PublicAccessChanged = "public-access.changed";
}

/// <summary>Reads of the audit log that more than one feature makes.</summary>
public static class AuditEntryQueries
{
    /// <summary>
    /// The upload.submitted entries naming this job, newest first, so the first is the one that says who submitted
    /// it. The id breaks ties, so every reader takes the same one.
    /// </summary>
    public static IOrderedQueryable<AuditEntry> SubmissionsOf(this IQueryable<AuditEntry> entries, Guid jobId) =>
        entries
            .Where(e => e.Action == AuditActions.UploadSubmitted && e.SubjectId == jobId)
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id);
}

/// <summary>The kinds of subject an audit entry names, as written in <see cref="AuditEntry.SubjectType"/>.</summary>
public static class AuditSubjectTypes
{
    public const string Job = "job";
    public const string Work = "work";
    public const string PublicAccess = "public-access";
}
