using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Ingest.ExternalIds;
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
        var joined = false;
        string? filingAuthor = null;

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
                work = await FindWorkByIsbnAsync(db, library, metadata!, cancellationToken);
                joined = work is not null;
                if (work is null)
                {
                    (work, filingAuthor) = await BuildWorkAsync(db, library, metadata!, payload.FileName, cancellationToken);
                }
            })
            .AddStep("file-item", async cancellationToken =>
            {
                // A copy of a work already here goes beside its other files, under the work's own title, and takes a
                // numbered name rather than replace a file of the same name. A new work gets a folder of its own,
                // numbered when another work already has the one its author and title make.
                var book = new Filing.StagedBook(stagedFile, work!.Title, handler!.Extension, library!, storage);
                var filed = joined
                    ? Filing.MoveBesideOthers(book, await FindWorkFolderAsync(db, work, cancellationToken))
                    : Filing.MoveIntoNewFolder(book, Filing.WorkFolder(filingAuthor, work.Title));

                string? coverPath = null;
                try
                {
                    // A work keeps the cover it has; only one without a cover takes the file's.
                    if (cover is not null && string.IsNullOrEmpty(work.CoverHash))
                    {
                        var folder = Path.GetDirectoryName(filed.FullPath)!;
                        var path = Path.Combine(folder, "cover" + cover.Extension);
                        await using (var stream = NookStorage.CreateFile(path))
                        {
                            coverPath = path;
                            await stream.WriteAsync(cover.Bytes, cancellationToken);
                        }

                        work.CoverHash = Convert.ToHexStringLower(SHA256.HashData(cover.Bytes));
                    }

                    var item = new Item { WorkId = work.Id, Format = handler.Format };
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

                    await db.SaveChangesAsync(cancellationToken);
                    run.SetResult(new IngestResult(IngestOutcome.Created, item.Id));
                }
                catch
                {
                    // Nothing was saved, so the file goes back to staging for a retry, and neither a cover nor the
                    // work's own folder, when this run made it, is left unclaimed.
                    Undo(filed, stagedFile, coverPath, logger);
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
    /// The oldest work in the library that holds one of the file's ISBNs, tracked so filing can give it a cover it
    /// lacks, or null when none does or the file gives no valid ISBN.
    /// </summary>
    private static async Task<Work?> FindWorkByIsbnAsync(
        NookDbContext db, Library library, BookMetadata metadata, CancellationToken cancellationToken)
    {
        var isbns = metadata.Identifiers
            .Where(i => i.Scheme == ExternalIdRegistry.Isbn)
            .Select(i => ExternalIdRegistry.Normalise(ExternalIdRegistry.Isbn, i.Value))
            .OfType<string>()
            .Distinct()
            .ToList();
        if (isbns.Count == 0)
        {
            return null;
        }

        return await db.Works
            .Where(w => w.LibraryId == library.Id
                && w.ExternalIds.Any(e => e.Scheme == ExternalIdRegistry.Isbn && isbns.Contains(e.Value)))
            .OrderBy(w => w.CreatedAt)
            .ThenBy(w => w.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// The <c>/</c>-separated folder, relative to the library's folder, that holds the work's earliest part. A work
    /// with no part yet gets the folder its own title and first author make.
    /// </summary>
    private static async Task<string> FindWorkFolderAsync(NookDbContext db, Work work, CancellationToken cancellationToken)
    {
        var earliest = await (
            from part in db.Parts
            join item in db.Items on part.ItemId equals item.Id
            where item.WorkId == work.Id
            orderby part.CreatedAt, part.Id
            select part.Path).FirstOrDefaultAsync(cancellationToken);
        if (earliest is not null)
        {
            return Filing.FolderOf(earliest);
        }

        var author = await (
            from contributor in db.WorkContributors
            join person in db.People on contributor.PersonId equals person.Id
            where contributor.WorkId == work.Id && contributor.Role == ContributorRole.Author
            orderby contributor.Order, contributor.Id
            select person.Name).FirstOrDefaultAsync(cancellationToken);
        return Filing.WorkFolder(author, work.Title);
    }

    /// <summary>
    /// Builds a new work in the library from what the file says, with its people, series, tags and external ids, and
    /// adds it all to the context unsaved. A person, series or tag whose name differs from one already stored only in
    /// case or spacing is that one, left as it is; any other is new, stored under its name tidied.
    /// </summary>
    private static async Task<(Work Work, string? FilingAuthor)> BuildWorkAsync(
        NookDbContext db, Library library, BookMetadata metadata, string fileName, CancellationToken cancellationToken)
    {
        var title = metadata.Title ?? TitleFromFileName(fileName);
        var work = new Work
        {
            LibraryId = library.Id,
            Title = title,
            SortTitle = metadata.SortTitle ?? title,
            Description = metadata.Description,
            Publisher = metadata.Publisher,
            Language = metadata.Language,
            PublicationDate = metadata.PublicationDate,
        };

        // Creators in a role Nook does not keep are left out, and those kept are numbered in file order without gaps.
        // A creator the file names more than once is one person, who takes each role on the work once. A new person
        // takes the sort name of the creator's first appearance.
        var contributors = Contributors(metadata).ToList();
        var people = await FindOrAddByNameAsync(
            db.People,
            contributors.Select(c => c.Creator.Name),
            name => new Person
            {
                Name = NameKey.Tidy(name),
                SortName = contributors.First(c => c.Creator.Name == name).Creator.SortName ?? NameKey.Tidy(name),
            },
            cancellationToken);
        var roles = new HashSet<(Guid PersonId, ContributorRole Role)>();
        string? filingAuthor = null;
        var order = 0;
        foreach (var (creator, role) in contributors)
        {
            var person = people[NameKey.Of(creator.Name)];
            if (!roles.Add((person.Id, role)))
            {
                continue;
            }

            // The folder takes the first author's name as the person row has it, so one person has one folder.
            if (role == ContributorRole.Author)
            {
                filingAuthor ??= person.Name;
            }

            work.Contributors.Add(new WorkContributor
            {
                WorkId = work.Id,
                PersonId = person.Id,
                Role = role,
                Order = order++,
            });
        }

        if (metadata.Series is { } bookSeries)
        {
            var series = await FindOrAddByNameAsync(
                db.Series, [bookSeries.Name], name => new Series { Name = NameKey.Tidy(name) },
                cancellationToken);
            work.SeriesId = series[NameKey.Of(bookSeries.Name)].Id;
            work.SeriesPosition = bookSeries.Position;
        }

        // A file that lists a subject more than once gets one tag for it.
        var tags = await FindOrAddByNameAsync(
            db.Tags, metadata.Subjects, name => new Tag { Name = NameKey.Tidy(name) }, cancellationToken);
        foreach (var subject in metadata.Subjects.Select(NameKey.Of).Distinct())
        {
            work.Tags.Add(tags[subject]);
        }

        // An ISBN the registry rejects is left out, and an identifier the file repeats is stored once.
        var externalIds = new HashSet<(string Scheme, string Value)>();
        foreach (var identifier in metadata.Identifiers)
        {
            if (ExternalIdRegistry.Normalise(identifier.Scheme, identifier.Value) is { } value
                && externalIds.Add((identifier.Scheme, value)))
            {
                work.ExternalIds.Add(new WorkExternalId { WorkId = work.Id, Scheme = identifier.Scheme, Value = value });
            }
        }

        db.Works.Add(work);
        return (work, filingAuthor);
    }

    /// <summary>
    /// The row for each of the names, by name key: the oldest row whose name has that key, or a new one, made from
    /// the first of the names with the key and added unsaved. The database narrows the rows by its own lowercasing
    /// of their names, which can differ from the key's for a few letters, so the key is worked out again here and
    /// decides.
    /// </summary>
    private static async Task<Dictionary<string, T>> FindOrAddByNameAsync<T>(
        DbSet<T> rows, IEnumerable<string> names, Func<string, T> create, CancellationToken cancellationToken)
        where T : Entity, INamed
    {
        var firstNames = new Dictionary<string, string>();
        foreach (var name in names)
        {
            firstNames.TryAdd(NameKey.Of(name), name);
        }

        var keys = firstNames.Keys.ToList();
        var candidates = keys.Count == 0
            ? []
            : await rows
                .Where(row => keys.Contains(row.Name.ToLower()))
                .ToListAsync(cancellationToken);
        var byKey = candidates
            .GroupBy(row => NameKey.Of(row.Name))
            .ToDictionary(group => group.Key, group => group.MinBy(row => (row.CreatedAt, row.Id))!);
        foreach (var (key, name) in firstNames)
        {
            if (!byKey.ContainsKey(key))
            {
                var row = create(name);
                rows.Add(row);
                byKey[key] = row;
            }
        }

        return byKey;
    }

    /// <summary>The file's creators whose role Nook keeps, in file order, each with that role.</summary>
    private static IEnumerable<(BookCreator Creator, ContributorRole Role)> Contributors(BookMetadata metadata) =>
        from creator in metadata.Creators
        let role = RoleOf(creator.Role)
        where role is not null
        select (creator, role.Value);

    /// <summary>
    /// The contributor role a MARC relator code stands for, compared ignoring case and surrounding spaces. No code
    /// means an author; a code for a role Nook does not keep gives null.
    /// </summary>
    private static ContributorRole? RoleOf(string? code) =>
        code?.Trim().ToLowerInvariant() switch
        {
            null or "" or "aut" => ContributorRole.Author,
            "trl" => ContributorRole.Translator,
            "edt" => ContributorRole.Editor,
            "nrt" => ContributorRole.Narrator,
            _ => null,
        };

    /// <summary>
    /// The title of a book whose file gives none: the uploaded name without any folder or extension, or
    /// <c>Untitled</c> when nothing is left. The name comes from the client, so a folder in it, written with either
    /// kind of slash, is dropped whatever the server's own separator.
    /// </summary>
    private static string TitleFromFileName(string fileName)
    {
        var name = fileName[(fileName.LastIndexOfAny(['/', '\\']) + 1)..];
        var title = Path.GetFileNameWithoutExtension(name).Trim();
        return title.Length == 0 ? Filing.Untitled : title;
    }

    /// <summary>
    /// Moves the filed book back to staging, deletes the cover this run wrote, and removes the work's folder when this
    /// run made it and that leaves it empty; an author folder made on the way stays. A failure here is logged, so the error that caused the undo is the one reported.
    /// </summary>
    private static void Undo(Filing.FiledBook filed, string stagedFile, string? coverPath, ILogger logger)
    {
        try
        {
            File.Move(filed.FullPath, stagedFile);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception, "Could not move {BookPath} back to staging at {StagedFile}.", filed.FullPath, stagedFile);
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
