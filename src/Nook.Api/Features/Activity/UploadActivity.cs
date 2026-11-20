using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Features.Audit;
using Nook.Api.Ingest;
using Nook.Api.Jobs;

namespace Nook.Api.Features.Activity;

/// <summary>An upload's uploader, and its row when it still has one.</summary>
/// <param name="UserId">The user who submitted the upload, or null when the submitted entry names none.</param>
/// <param name="Row">The upload as listed, or null when its job is gone and no outcome says what became of it.</param>
public record JobUpload(Guid? UserId, UploadActivityResponse? Row);

/// <summary>
/// Reads uploads. An upload is its <c>upload.submitted</c> entry plus its job: the job gives its live status, and
/// the outcome entries are what remains once the job is deleted. Entries are found by their submission id, never
/// through their details.
/// </summary>
public sealed class UploadActivity(NookDbContext db)
{
    private static readonly string[] OutcomeActions =
        [AuditActions.UploadAdded, AuditActions.UploadDuplicate, AuditActions.UploadFailed];

    /// <summary>
    /// The user's most recent uploads, newest first. A submission with no job and no outcome is left out, and the
    /// limit counts the rows that remain, so the query reads on until it has that many or runs out.
    /// </summary>
    public async Task<IReadOnlyList<UploadActivityResponse>> ForUserAsync(
        Guid userId, int limit, CancellationToken cancellationToken = default)
    {
        var rows = new List<UploadActivityResponse>();
        AuditEntry? last = null;
        while (rows.Count < limit)
        {
            var submissions = db.AuditEntries
                .AsNoTracking()
                .Where(e => e.Action == AuditActions.UploadSubmitted && e.ActorId == userId);
            if (last is not null)
            {
                // Each batch starts after the last entry read, not at a count of entries, so an upload sent in
                // between batches cannot push one already read into the next.
                var (at, id) = (last.CreatedAt, last.Id);
                submissions = submissions.Where(e => e.CreatedAt < at || (e.CreatedAt == at && e.Id.CompareTo(id) < 0));
            }

            var batch = await submissions
                .OrderByDescending(e => e.CreatedAt)
                .ThenByDescending(e => e.Id)
                .Take(limit)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            last = batch[^1];
            foreach (var row in await BuildRowsAsync(batch, cancellationToken))
            {
                if (row is not null && rows.Count < limit)
                {
                    rows.Add(row);
                }
            }
        }

        return rows;
    }

    /// <summary>
    /// The upload whose job this is, with the id of the user who submitted it. The row is null when the job is gone
    /// and no outcome remains; the whole result is null when no upload was submitted for the job.
    /// </summary>
    public async Task<JobUpload?> ForJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var submission = await db.AuditEntries
            .AsNoTracking()
            .SubmissionsOf(jobId)
            .FirstOrDefaultAsync(cancellationToken);
        if (submission is null)
        {
            return null;
        }

        var rows = await BuildRowsAsync([submission], cancellationToken);
        return new JobUpload(submission.ActorId, rows[0]);
    }

    /// <summary>One row per submission, in order, each null when nothing says where the upload stands.</summary>
    private async Task<List<UploadActivityResponse?>> BuildRowsAsync(
        List<AuditEntry> submissions, CancellationToken cancellationToken)
    {
        var jobIds = submissions.Select(s => s.SubjectId).OfType<Guid>().ToList();
        var jobs = await db.Jobs
            .AsNoTracking()
            .Where(j => jobIds.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, cancellationToken);

        var submissionIds = submissions.Select(s => s.Id).ToList();
        var outcomes = (await db.AuditEntries
                .AsNoTracking()
                .Where(e => e.SubmissionId != null && submissionIds.Contains(e.SubmissionId.Value)
                    && OutcomeActions.Contains(e.Action))
                .OrderByDescending(e => e.CreatedAt)
                .ThenByDescending(e => e.Id)
                .ToListAsync(cancellationToken))
            .ToLookup(e => e.SubmissionId!.Value);

        var rows = new List<UploadActivityResponse?>(submissions.Count);
        foreach (var submission in submissions)
        {
            var job = submission.SubjectId is { } id ? jobs.GetValueOrDefault(id) : null;
            rows.Add(BuildRow(submission, job, outcomes[submission.Id].ToList()));
        }

        return rows;
    }

    /// <param name="outcomes">The upload's outcome entries, newest first.</param>
    private static UploadActivityResponse? BuildRow(AuditEntry submission, Job? job, List<AuditEntry> outcomes)
    {
        var finished = outcomes.FirstOrDefault(
            e => e.Action is AuditActions.UploadAdded or AuditActions.UploadDuplicate);
        var failed = outcomes.FirstOrDefault(e => e.Action == AuditActions.UploadFailed);

        UploadActivityStatus status;
        Guid? workId = null;
        UploadFailureKind? failure = null;
        switch (job?.Status)
        {
            case JobStatus.Pending:
                status = UploadActivityStatus.Queued;
                break;
            case JobStatus.Running:
                status = UploadActivityStatus.Running;
                break;
            case JobStatus.Succeeded:
                if (ReadResult(job.Result) is { } result)
                {
                    status = result.Outcome == IngestOutcome.Duplicate
                        ? UploadActivityStatus.Duplicate
                        : UploadActivityStatus.Added;
                    workId = result.WorkId;
                    break;
                }

                return FromFinished(submission, finished);
            case JobStatus.Failed:
                status = UploadActivityStatus.Failed;
                failure = ReadKind(failed);
                break;
            default:
                return FromFinished(submission, finished);
        }

        return Row(submission, status, workId, failure);
    }

    private static UploadActivityResponse? FromFinished(AuditEntry submission, AuditEntry? finished) =>
        finished is null
            ? null
            : Row(
                submission,
                finished.Action == AuditActions.UploadDuplicate ? UploadActivityStatus.Duplicate : UploadActivityStatus.Added,
                finished.SubjectId,
                failure: null);

    private static UploadActivityResponse Row(
        AuditEntry submission, UploadActivityStatus status, Guid? workId, UploadFailureKind? failure)
    {
        var details = ReadSubmitted(submission);
        return new UploadActivityResponse(
            submission.SubjectId ?? Guid.Empty,
            submission.SubjectName,
            details.SectionId,
            details.SectionName,
            submission.CreatedAt,
            status,
            workId,
            failure);
    }

    private static IngestResult? ReadResult(string? json) => Read<IngestResult?>(json, null);

    private static UploadFailureKind ReadKind(AuditEntry? failed) =>
        Read<FailedDetails?>(failed?.Details, null)?.Kind ?? UploadFailureKind.Internal;

    private static SubmittedDetails ReadSubmitted(AuditEntry submission) =>
        Read(submission.Details, new SubmittedDetails(null, null));

    /// <summary>What the JSON holds, or the fallback when there is none or it cannot be read.</summary>
    private static T Read<T>(string? json, T fallback)
    {
        try
        {
            return json is null ? fallback : JobJson.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private sealed record SubmittedDetails(Guid? SectionId, string? SectionName);

    private sealed record FailedDetails(UploadFailureKind Kind);
}
