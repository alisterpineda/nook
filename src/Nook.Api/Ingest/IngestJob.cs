using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Jobs;
using Nook.Api.Storage;

namespace Nook.Api.Ingest;

/// <summary>
/// Ingests one staged file into a library. The staged file stays where it is while the job can still be retried, and
/// every row the job makes is saved at once in its last step, after the file is filed.
/// </summary>
public sealed class IngestJob : JobType
{
    public const string TypeName = "ingest";

    /// <summary>The author folder of a book whose file names no author.</summary>
    public const string UnknownAuthor = "Unknown Author";

    public override string Name => TypeName;

    public override JobRun CreateRun(string payloadJson, IServiceProvider services)
    {
        var payload = JobJson.Deserialize<IngestPayload>(payloadJson);
        var storage = services.GetRequiredService<NookStorage>();
        var handlers = services.GetServices<IFormatHandler>().ToList();
        var db = services.GetRequiredService<NookDbContext>();
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
        Work? work = null;

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

                // The same bytes already in this library: the oldest item holding them stands for the upload.
                var hash = sha256;
                var existing = await (
                    from part in db.Parts
                    join item in db.Items on part.ItemId equals item.Id
                    join existingWork in db.Works on item.WorkId equals existingWork.Id
                    where part.Sha256 == hash && existingWork.LibraryId == payload.LibraryId
                    orderby item.CreatedAt, item.Id
                    select (Guid?)item.Id).FirstOrDefaultAsync(cancellationToken);
                if (existing is { } itemId)
                {
                    stream.Close();
                    File.Delete(stagedFile);
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
                library = await db.Libraries.SingleOrDefaultAsync(l => l.Id == payload.LibraryId, cancellationToken)
                    ?? throw new InvalidOperationException("The library the file was uploaded to no longer exists.");
                work = BuildWork(db, library, metadata!, payload.FileName);
            })
            .AddStep("file-item", async cancellationToken =>
            {
                var author = metadata!.Creators.FirstOrDefault() ?? UnknownAuthor;
                var relativePath = $"Books/{author}/{work!.Title}/{work.Title}{handler!.Extension}";
                var bookPath = storage.LibraryFile(library!, relativePath);
                var folder = Path.GetDirectoryName(bookPath)!;
                Directory.CreateDirectory(folder);

                // Moving never replaces a file already there; the move fails instead, and the staged file stays.
                File.Move(stagedFile, bookPath);
                string? coverPath = null;
                try
                {
                    if (cover is not null)
                    {
                        var path = Path.Combine(folder, "cover" + cover.Extension);
                        await using (var stream = NookStorage.CreateFile(path))
                        {
                            coverPath = path;
                            await stream.WriteAsync(cover.Bytes, cancellationToken);
                        }

                        work.CoverHash = Convert.ToHexStringLower(SHA256.HashData(cover.Bytes));
                    }

                    var item = new Item { WorkId = work.Id, Format = handler.Format };
                    work.Items.Add(item);
                    db.Parts.Add(new Part
                    {
                        ItemId = item.Id,
                        Order = 0,
                        Path = relativePath,
                        MediaType = handler.MediaType,
                        Size = size,
                        Sha256 = sha256!,
                    });

                    await db.SaveChangesAsync(cancellationToken);
                    run.SetResult(new IngestResult(IngestOutcome.Created, item.Id));
                }
                catch
                {
                    // Nothing was saved, so the file goes back to staging for a retry and no cover is left unclaimed.
                    Undo(bookPath, stagedFile, coverPath, logger);
                    throw;
                }
            });
    }

    /// <summary>Deletes the staged file, which only this job used.</summary>
    public override Task OnDeletedAsync(string payloadJson, IServiceProvider services)
    {
        var payload = JobJson.Deserialize<IngestPayload>(payloadJson);
        File.Delete(services.GetRequiredService<NookStorage>().StagedFile(payload.StagedName));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds a new work in the library from what the file says, with new people, series and tags, and adds it all
    /// to the context unsaved.
    /// </summary>
    private static Work BuildWork(NookDbContext db, Library library, BookMetadata metadata, string fileName)
    {
        var title = metadata.Title ?? Path.GetFileNameWithoutExtension(fileName);
        var work = new Work
        {
            LibraryId = library.Id,
            Title = title,
            SortTitle = title,
            Description = metadata.Description,
            Publisher = metadata.Publisher,
            Language = metadata.Language,
            PublicationDate = metadata.PublicationDate,
        };

        for (var i = 0; i < metadata.Creators.Count; i++)
        {
            var name = metadata.Creators[i];
            var person = new Person { Name = name, SortName = name };
            db.People.Add(person);
            work.Contributors.Add(new WorkContributor
            {
                WorkId = work.Id,
                PersonId = person.Id,
                Role = ContributorRole.Author,
                Order = i,
            });
        }

        if (metadata.Series is { } bookSeries)
        {
            var series = new Series { Name = bookSeries.Name };
            db.Series.Add(series);
            work.SeriesId = series.Id;
            work.SeriesPosition = bookSeries.Position;
        }

        // A file that lists a subject twice gets one tag for it.
        foreach (var subject in metadata.Subjects.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            work.Tags.Add(new Tag { Name = subject });
        }

        db.Works.Add(work);
        return work;
    }

    /// <summary>Moves the filed book back to staging and deletes the cover this run wrote. A failure here is logged, so the error that caused the undo is the one reported.</summary>
    private static void Undo(string bookPath, string stagedFile, string? coverPath, ILogger logger)
    {
        try
        {
            File.Move(bookPath, stagedFile);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not move {BookPath} back to staging at {StagedFile}.", bookPath, stagedFile);
        }

        if (coverPath is null)
        {
            return;
        }

        try
        {
            File.Delete(coverPath);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not delete the cover {CoverPath}.", coverPath);
        }
    }
}
