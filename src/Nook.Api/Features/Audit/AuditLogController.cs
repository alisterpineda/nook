using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;

namespace Nook.Api.Features.Audit;

/// <summary>The audit log, for admins to read.</summary>
[ApiController]
[Route("api/audit-log")]
[Authorize(Roles = nameof(BuiltInRole.Admin))]
public class AuditLogController(NookDbContext db) : ControllerBase
{
    /// <summary>One page of the entries, narrowed to an actor and an action when given, newest first.</summary>
    [HttpGet]
    [ProducesResponseType<Page<AuditEntryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<Page<AuditEntryResponse>>> List(
        [FromQuery] AuditLogQuery query, CancellationToken cancellationToken)
    {
        var entries = db.AuditEntries.AsNoTracking();
        if (query.ActorId is { } actorId)
        {
            entries = entries.Where(e => e.ActorId == actorId);
        }

        if (!string.IsNullOrEmpty(query.Action))
        {
            entries = entries.Where(e => e.Action == query.Action);
        }

        var total = await entries.CountAsync(cancellationToken);

        // The id breaks ties, so an entry never shows on two pages or on none.
        var rows = await entries
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Skip(query.Offset)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);

        // Details is passed through as the JSON its action wrote.
        var items = rows
            .Select(e => new AuditEntryResponse(
                e.Id,
                e.CreatedAt,
                e.ActorId,
                e.ActorName,
                e.ActorEmail,
                e.Action,
                e.SubjectType,
                e.SubjectId,
                e.SubjectName,
                JsonSerializer.Deserialize<JsonElement>(e.Details)))
            .ToList();

        return new Page<AuditEntryResponse>(items, total, query.Offset, query.Limit);
    }

    /// <summary>
    /// The actors and actions in the log. Each actor is listed once, under the name and email of their newest entry;
    /// entries with no actor id name no actor.
    /// </summary>
    [HttpGet("filters")]
    [ProducesResponseType<AuditFiltersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuditFiltersResponse>> Filters(CancellationToken cancellationToken)
    {
        var actors = await db.AuditEntries
            .Where(e => e.ActorId != null)
            .Select(e => e.ActorId!.Value)
            .Distinct()
            .Select(id => db.AuditEntries
                .Where(e => e.ActorId == id)
                .OrderByDescending(e => e.CreatedAt)
                .ThenByDescending(e => e.Id)
                .Select(e => new AuditActorResponse(id, e.ActorName, e.ActorEmail))
                .First())
            .ToListAsync(cancellationToken);

        var actions = await db.AuditEntries
            .Select(e => e.Action)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync(cancellationToken);

        return new AuditFiltersResponse(
            actors.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.Id).ToList(),
            actions);
    }
}
