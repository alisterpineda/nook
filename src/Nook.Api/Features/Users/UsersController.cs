using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;

namespace Nook.Api.Features.Users;

/// <summary>The people who have an account, for admins to read.</summary>
[ApiController]
[Route("api/users")]
[Authorize(Roles = nameof(BuiltInRole.Admin))]
public class UsersController(NookDbContext db) : ControllerBase
{
    /// <summary>One page of the users, in the order asked for, by shown name ignoring case unless said otherwise.</summary>
    [HttpGet]
    [ProducesResponseType<Page<UserSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<Page<UserSummaryResponse>>> List(
        [FromQuery] UserListQuery query, CancellationToken cancellationToken)
    {
        var total = await db.Users.CountAsync(cancellationToken);

        // The shown name is NookUser.ShownName, worked out in SQL; the id breaks ties, ascending whatever the direction, so a user never shows on two
        // pages or on none.
        var rows = db.Users
            .AsNoTracking()
            .Join(db.Roles, u => u.RoleId, r => r.Id, (u, r) => new { User = u, Role = r.Name })
            .Select(x => new
            {
                x.User,
                x.Role,
                ShownEmail = x.User.Email ?? x.User.UserName ?? "",
            })
            .Select(x => new
            {
                x.User.Id,
                x.User.CreatedAt,
                x.Role,
                x.ShownEmail,
                HasPassword = x.User.PasswordHash != null,
                HasSingleSignOn = db.UserLogins.Any(l => l.UserId == x.User.Id),
                HasAuditEntries = db.AuditEntries.Any(a => a.ActorId == x.User.Id),
                ShownName = !string.IsNullOrWhiteSpace(x.User.DisplayName)
                    ? x.User.DisplayName
                    : x.ShownEmail.IndexOf("@") > 0
                        ? x.ShownEmail.Substring(0, x.ShownEmail.IndexOf("@"))
                        : x.ShownEmail,
            });

        var ordered = (query.Sort, query.Direction) switch
        {
            (UserSort.Name, SortDirection.Asc) => rows.OrderBy(x => x.ShownName.ToLower()),
            (UserSort.Name, _) => rows.OrderByDescending(x => x.ShownName.ToLower()),
            (UserSort.Email, SortDirection.Asc) => rows.OrderBy(x => x.ShownEmail.ToLower()),
            (UserSort.Email, _) => rows.OrderByDescending(x => x.ShownEmail.ToLower()),
            (UserSort.Role, SortDirection.Asc) => rows.OrderBy(x => x.Role),
            (UserSort.Role, _) => rows.OrderByDescending(x => x.Role),
            (UserSort.Joined, SortDirection.Asc) => rows.OrderBy(x => x.CreatedAt),
            (UserSort.Joined, _) => rows.OrderByDescending(x => x.CreatedAt),
        };

        var items = await ordered
            .ThenBy(x => x.Id)
            .Skip(query.Offset)
            .Take(query.Limit)
            .Select(x => new UserSummaryResponse(x.Id, x.ShownName, x.ShownEmail, x.Role, x.CreatedAt,
                x.HasPassword, x.HasSingleSignOn, x.HasAuditEntries))
            .ToListAsync(cancellationToken);

        return new Page<UserSummaryResponse>(items, total, query.Offset, query.Limit);
    }
}
