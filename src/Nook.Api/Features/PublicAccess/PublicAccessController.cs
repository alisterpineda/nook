using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Features.Audit;

namespace Nook.Api.Features.PublicAccess;

/// <summary>What a visitor with no account may do, for admins to read and change.</summary>
[ApiController]
[Route("api/public-access")]
[Authorize(Roles = nameof(BuiltInRole.Admin))]
public class PublicAccessController(NookDbContext db, InstanceSettings settings, AuditLog audit) : ControllerBase
{
    /// <summary>Every public access setting, with each section by name.</summary>
    [HttpGet]
    [ProducesResponseType<PublicAccessResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PublicAccessResponse>> Get(CancellationToken cancellationToken) =>
        await ReadAsync(cancellationToken);

    private async Task<PublicAccessResponse> ReadAsync(CancellationToken cancellationToken)
    {
        var enabled = await settings.PublicAccessEnabledAsync(cancellationToken);
        var anonymous = await db.Roles.AsNoTracking().SingleAsync(r => r.Id == Role.AnonymousId, cancellationToken);
        var levels = await db.RoleSections
            .Where(r => r.RoleId == Role.AnonymousId)
            .ToDictionaryAsync(r => r.SectionId, r => r.Access, cancellationToken);
        var sections = await db.Sections
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(cancellationToken);

        return new PublicAccessResponse(
            enabled,
            anonymous.ContentAccess,
            anonymous.DefaultAreaAccess,
            sections
                .Select(s => new PublicAccessSectionResponse(s.Id, s.Name, levels.GetValueOrDefault(s.Id, ContentAccess.None)))
                .ToList());
    }

    /// <summary>
    /// Replaces every public access setting at once. A section left out, or given none, has no level. A level above
    /// the Anonymous role's own is kept as given, and the Anonymous level caps it where it is used.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Save(PublicAccessRequest request, CancellationToken cancellationToken)
    {
        // Each section holds one level, so a section named twice could not be saved as asked.
        if (request.Sections.DistinctBy(s => s.Id).Count() < request.Sections.Count)
        {
            ModelState.AddModelError(nameof(request.Sections), "A section is given more than one level.");
            return ValidationProblem(ModelState);
        }

        // The contract names four levels, and a number outside them would be saved and read as above download.
        if (!Enum.IsDefined(request.Access))
        {
            ModelState.AddModelError(nameof(request.Access), "The level is not one of none, browse, read and download.");
        }

        if (!Enum.IsDefined(request.DefaultAreaAccess))
        {
            ModelState.AddModelError(
                nameof(request.DefaultAreaAccess), "The level is not one of none, browse, read and download.");
        }

        if (request.Sections.Any(s => !Enum.IsDefined(s.Access)))
        {
            ModelState.AddModelError(
                nameof(request.Sections), "A section's level is not one of none, browse, read and download.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        // Chosen once, outside the retry strategy, so a pass that runs again after a commit whose answer was lost finds
        // the entry the commit kept.
        var entryId = Guid.CreateVersion7();
        var opened = request.Sections.Where(s => s.Access != ContentAccess.None).ToList();
        var sectionIds = request.Sections.Select(s => s.Id).ToList();

        // The database is set to retry, so the transaction has to run inside its retry strategy. Every attempt writes
        // the same values, so one that runs after a commit whose answer was lost changes nothing.
        var unknown = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // A retry starts from what the database holds, not from what a failed attempt loaded.
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // Holds the Anonymous role's row until the commit, so two saves at once run one after the other and
            // neither adds a row the other has just added.
            var anonymous = (await db.Roles
                    .FromSql($"SELECT * FROM roles WHERE id = {Role.AnonymousId} FOR UPDATE")
                    .ToListAsync(cancellationToken))
                .Single();

            var known = await db.Sections
                .Where(s => sectionIds.Contains(s.Id))
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);
            var missing = sectionIds.Except(known).ToList();
            if (missing.Count > 0)
            {
                return missing;
            }

            var written = await db.AuditEntries.AnyAsync(e => e.Id == entryId, cancellationToken);
            var before = written ? null : await ReadAsync(cancellationToken);

            anonymous.ContentAccess = request.Access;
            anonymous.DefaultAreaAccess = request.DefaultAreaAccess;

            await db.RoleSections.Where(r => r.RoleId == Role.AnonymousId).ExecuteDeleteAsync(cancellationToken);
            db.RoleSections.AddRange(opened.Select(s => new RoleSection
            {
                RoleId = Role.AnonymousId,
                SectionId = s.Id,
                Access = s.Access,
            }));

            // Saves the role and its sections along with the switch.
            await settings.SetPublicAccessEnabledAsync(request.Enabled, cancellationToken);

            if (!written)
            {
                await audit.AddAsync(
                    User,
                    AuditActions.PublicAccessChanged,
                    new AuditSubject(AuditSubjectTypes.PublicAccess, null, "Public access"),
                    new { before, after = await ReadAsync(cancellationToken) },
                    id: entryId,
                    cancellationToken: cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new List<Guid>();
        });

        if (unknown.Count > 0)
        {
            foreach (var id in unknown)
            {
                ModelState.AddModelError(nameof(request.Sections), $"There is no section with the id {id}.");
            }

            return ValidationProblem(ModelState);
        }

        return NoContent();
    }
}
