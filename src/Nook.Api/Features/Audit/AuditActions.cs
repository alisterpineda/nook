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
    public const string PublicAccessChanged = "public-access.changed";
}

/// <summary>The kinds of subject an audit entry names, as written in <see cref="AuditEntry.SubjectType"/>.</summary>
public static class AuditSubjectTypes
{
    public const string Job = "job";
    public const string Work = "work";
    public const string PublicAccess = "public-access";
}
