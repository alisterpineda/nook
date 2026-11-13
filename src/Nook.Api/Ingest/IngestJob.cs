using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nook.Api.Data;
using Nook.Api.Jobs;
using Nook.Api.Storage;

namespace Nook.Api.Ingest;

/// <summary>
/// Ingests one staged file into a library. The staged file is the run's input and outlives the run: filing copies
/// it into the library, and it is deleted only once the job is recorded as succeeded, so a run that fails or whose
/// worker dies at any point can run again from the top. The last step finds or makes the rows, files a copy and
/// saves every row at once, all under <see cref="IngestLock"/>, so ingests running side by side take turns there and
/// see each other's rows.
/// </summary>
public sealed class IngestJob : JobType
{
    public const string TypeName = "ingest";

    public override string Name => TypeName;

    public override JobRun CreateRun(string payloadJson, IServiceProvider services)
    {
        var payload = JobJson.Deserialize<IngestPayload>(payloadJson);
        var storage = services.GetRequiredService<NookStorage>();
        var handlers = services.GetServices<IFormatHandler>().ToList();
        var db = services.GetRequiredService<NookDbContext>();
        var matcher = new WorkMatcher(db);
        var logger = services.GetRequiredService<ILogger<IngestJob>>();
        var stagedFile = storage.StagedFile(payload.StagedName);
        var run = new JobRun();

        // What each step learns, for the steps after it.
        IFormatHandler? handler = null;
        string? sha256 = null;
        long size = 0;
        BookMetadata? metadata = null;
        BookCover? cover = null;
        Library? library = null;
        WorkDraft? draft = null;

        // The item the last step built and went on to save. It outlives a pass of the retry strategy, so a pass after
        // a commit that landed but reported a failure finds the item saved instead of filing the book again.
        Guid? builtItemId = null;

        return run
            .AddStep("identify-format", async cancellationToken =>
            {
                if (!File.Exists(stagedFile))
                {
                    throw new FileNotFoundException($"The uploaded file '{payload.FileName}' is no longer in staging.");
                }

                // The format is judged by the content: the uploaded name may say anything.
                foreach (var candidate in handlers)
                {
                    if (await candidate.MatchesAsync(stagedFile, cancellationToken))
                    {
                        handler = candidate;
                        return;
                    }
                }

                throw new InvalidOperationException($"The uploaded file '{payload.FileName}' is in an unsupported format.");
            })
            .AddStep("check-duplicate", async cancellationToken =>
            {
                await using var stream = NookStorage.OpenFile(stagedFile);
                sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
                size = stream.Length;

                // The same bytes already in this library: the oldest item holding them stands for the upload. A copy
                // being filed at the same moment is caught again under the lock in the last step. A run whose worker
                // died after saving ends here too, as a duplicate of the item it made.
                if (await matcher.FindItemWithHashAsync(payload.LibraryId, sha256, cancellationToken) is { } itemId)
                {
                    run.Complete(new IngestResult(IngestOutcome.Duplicate, itemId));
                }
            })
            .AddStep("extract-metadata", async cancellationToken =>
            {
                metadata = await handler!.ReadMetadataAsync(stagedFile, cancellationToken);
            })
            .AddStep("extract-cover", async cancellationToken =>
            {
                cover = await handler!.ReadCoverAsync(stagedFile, cancellationToken);
            })
            .AddStep("match-work", async cancellationToken =>
            {
                // What the file says the work is. Finding or making its rows waits for the lock in the next step.
                library = await db.Libraries.SingleOrDefaultAsync(l => l.Id == payload.LibraryId, cancellationToken)
                    ?? throw new InvalidOperationException("The library the file was uploaded to no longer exists.");
                draft = WorkDraft.From(metadata!, payload.FileName);
            })
            .AddStep("file-item", cancellationToken =>
                // The database is set to retry, so the transaction has to run inside its retry strategy, and the
                // whole step is one pass of it.
                db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    // A retry starts from what the database holds, not from what a failed pass loaded or added.
                    db.ChangeTracker.Clear();
                    if (builtItemId is { } savedId && await IsItemSavedAsync(db, savedId, cancellationToken))
                    {
                        run.SetResult(new IngestResult(IngestOutcome.Created, savedId));
                        return;
                    }

                    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                    await IngestLock.TakeAsync(db, cancellationToken);
                    await FileUnderLockAsync(transaction, cancellationToken);
                }));

        // Runs with the lock held, so no other ingest matches or saves until this one commits or rolls back.
        async Task FileUnderLockAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
        {
            // Two copies of one file uploaded together both pass check-duplicate, so it is asked again here.
            if (await matcher.FindItemWithHashAsync(library!.Id, sha256!, cancellationToken) is { } existingId)
            {
                await transaction.CommitAsync(CancellationToken.None);
                run.Complete(new IngestResult(IngestOutcome.Duplicate, existingId));
                return;
            }

            var work = await matcher.FindByIsbnAsync(library, draft!, cancellationToken);
            var joined = work is not null;
            string? filingAuthor = null;
            if (work is null)
            {
                (work, filingAuthor) = await matcher.BuildAsync(library, draft!, cancellationToken);
            }

            // A copy of a work already here goes beside its other files, under the work's own title, and takes a
            // numbered name rather than replace a file a part points at. A new work gets a folder of its own,
            // numbered when a part of another work is already in the one its author and title make. A file or folder
            // no part points at, such as one a run that died left, is free. The parts are read from the author folder
            // down, which holds the work folder and its numbered siblings.
            var workFolder = joined
                ? await matcher.FindFolderAsync(work, cancellationToken)
                : Filing.WorkFolder(filingAuthor, work.Title);
            var claimedPaths = await matcher.ClaimedPathsAsync(library, Filing.FolderOf(workFolder), cancellationToken);
            var book = new Filing.StagedBook(stagedFile, work.Title, handler!.Extension, library, storage, claimedPaths);
            var filed = joined ? Filing.CopyBesideOthers(book, workFolder) : Filing.CopyIntoNewFolder(book, workFolder);

            var item = new Item { WorkId = work.Id, Format = handler.Format };
            string? coverPath = null;
            var saving = false;
            try
            {
                // A work keeps the cover it has; only one without a cover, as read under the lock, takes the file's,
                // over any cover of the same name a run that died left there.
                if (cover is not null && string.IsNullOrEmpty(work.CoverHash))
                {
                    var folder = Path.GetDirectoryName(filed.FullPath)!;
                    var path = Path.Combine(folder, "cover" + cover.Extension);
                    await using (var stream = NookStorage.ReplaceFile(path))
                    {
                        coverPath = path;
                        await stream.WriteAsync(cover.Bytes, cancellationToken);
                    }

                    work.CoverHash = Convert.ToHexStringLower(SHA256.HashData(cover.Bytes));
                }

                db.Items.Add(item);
                db.Parts.Add(new Part
                {
                    ItemId = item.Id,
                    Order = 0,
                    Path = filed.RelativePath,
                    MediaType = handler.MediaType,
                    Size = size,
                    Sha256 = sha256!,
                });

                builtItemId = item.Id;
                saving = true;
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(CancellationToken.None);
                run.SetResult(new IngestResult(IngestOutcome.Created, item.Id));
            }
            catch
            {
                // The staged file is still there for a retry or the strategy's next pass, so only the copy is at
                // stake. A failure before the save leaves the lock held, and the copy goes at once.
                if (saving)
                {
                    await UndoUnlessSavedAsync(transaction, item.Id, filed, coverPath);
                }
                else
                {
                    Undo(filed, coverPath, logger);
                }

                throw;
            }
        }

        // A save or commit that throws may still have landed, and a connection that broke has already let go of the
        // lock, so another ingest may have filed over the copy and claimed its path. The lock is given up for good
        // and taken again on a connection of its own; under it the copy goes only when this run's item is absent and
        // no part points at the path. When nothing says, the copy stays, unclaimed, for a later run to file over.
        async Task UndoUnlessSavedAsync(
            IDbContextTransaction transaction, Guid itemId, Filing.FiledBook filed, string? coverPath)
        {
            try
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch (Exception exception)
                {
                    // A transaction that has ended, by a commit that landed or a lost connection, holds no lock.
                    logger.LogDebug(exception, "The failed ingest transaction had already ended.");
                }

                await using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
                var fresh = scope.ServiceProvider.GetRequiredService<NookDbContext>();
                await fresh.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    await using var check = await fresh.Database.BeginTransactionAsync(CancellationToken.None);
                    await IngestLock.TakeAsync(fresh, CancellationToken.None);
                    if (await IsItemSavedAsync(fresh, itemId, CancellationToken.None))
                    {
                        return;
                    }

                    var claimedPaths = await new WorkMatcher(fresh).ClaimedPathsAsync(
                        library!, Filing.FolderOf(filed.RelativePath), CancellationToken.None);
                    if (!Filing.IsClaimed(claimedPaths, filed.RelativePath))
                    {
                        Undo(filed, coverPath, logger);
                    }
                });
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Could not tell whether item {ItemId} was saved, so its filed copy is left in place.",
                    itemId);
            }
        }

        static Task<bool> IsItemSavedAsync(NookDbContext context, Guid itemId, CancellationToken cancellationToken) =>
            context.Items.AnyAsync(i => i.Id == itemId, cancellationToken);
    }

    /// <summary>
    /// Deletes the staged file once the job is recorded as succeeded, whether it made an item or found a duplicate:
    /// the job can no longer run again, so nothing needs the file.
    /// </summary>
    public override Task OnSucceededAsync(string payloadJson, IServiceProvider services) =>
        DeleteStagedFileAsync(payloadJson, services);

    /// <summary>Deletes the staged file, which only this job used.</summary>
    public override Task OnDeletedAsync(string payloadJson, IServiceProvider services) =>
        DeleteStagedFileAsync(payloadJson, services);

    private static Task DeleteStagedFileAsync(string payloadJson, IServiceProvider services)
    {
        var payload = JobJson.Deserialize<IngestPayload>(payloadJson);
        File.Delete(services.GetRequiredService<NookStorage>().StagedFile(payload.StagedName));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Deletes the copy this run filed and the cover it wrote, and removes the work's folder when this run made it and
    /// that leaves it empty; an author folder made on the way stays. The staged file is not touched. A failure here is
    /// logged, so the error that caused the undo is the one reported.
    /// </summary>
    private static void Undo(Filing.FiledBook filed, string? coverPath, ILogger logger)
    {
        try
        {
            File.Delete(filed.FullPath);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not delete the filed copy {BookPath}.", filed.FullPath);
        }

        if (coverPath is not null)
        {
            try
            {
                File.Delete(coverPath);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not delete the cover {CoverPath}.", coverPath);
            }
        }

        if (filed.CreatedFolder is null)
        {
            return;
        }

        try
        {
            Filing.RemoveIfEmpty(filed.CreatedFolder);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not remove the folder {Folder}.", filed.CreatedFolder);
        }
    }
}
