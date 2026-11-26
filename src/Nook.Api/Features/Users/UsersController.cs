using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nook.Api.Data;
using Nook.Api.Features.Audit;
using Nook.Api.Features.Auth;

namespace Nook.Api.Features.Users;

/// <summary>The people who have an account, for admins to read, add, give passwords to, change the role of and delete.</summary>
[ApiController]
[Route("api/users")]
[Authorize(Roles = nameof(BuiltInRole.Admin))]
public class UsersController(NookDbContext db, UserManager<NookUser> users, AuditLog audit,
    IOptions<OidcOptions> oidc) : ControllerBase
{
    /// <summary>One page of the users, in the order asked for, by shown name ignoring case unless said otherwise.</summary>
    [HttpGet]
    [ProducesResponseType<Page<UserSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<Page<UserSummaryResponse>>> List(
        [FromQuery] UserListQuery query, CancellationToken cancellationToken)
    {
        var total = await db.Users.CountAsync(cancellationToken);

        var rows = Rows();

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

        var page = await ordered
            .ThenBy(x => x.Id)
            .Skip(query.Offset)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);

        return new Page<UserSummaryResponse>([.. page.Select(Summary)], total, query.Offset, query.Limit);
    }

    /// <summary>
    /// Adds a user who signs in with the email and password given, and holds the role. Not found when password sign-in
    /// is off.
    /// </summary>
    [HttpPost]
    [RequiresPasswordSignIn]
    [ProducesResponseType<UserSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserSummaryResponse>> Add(AddUserRequest request, CancellationToken cancellationToken)
    {
        // A missing role is caught by [Required], which leaves the role that cannot be given to a person, and a number
        // that names no role.
        if (request.Role is not { } role || role == BuiltInRole.Anonymous || !Enum.IsDefined(role))
        {
            ModelState.AddModelError(nameof(request.Role), "Choose admin, contributor or viewer.");
            return ValidationProblem(ModelState);
        }

        var user = new NookUser
        {
            UserName = request.Email,
            Email = request.Email,
            RoleId = Role.IdOf(role),
            DisplayName = NookUser.CleanDisplayName(request.DisplayName),
        };

        // The entry is saved by the same save as the user, which the UserManager call below makes.
        await audit.AddAsync(
            User,
            AuditActions.UserAdded,
            new AuditSubject(AuditSubjectTypes.User, user.Id, user.ShownName),
            new { email = user.ShownEmail, role = role.ToString() },
            cancellationToken: cancellationToken);

        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            ModelState.AddAccountErrors(result, nameof(request.Password), nameof(request.Email));

            return ValidationProblem(ModelState);
        }

        var row = await Rows().AsNoTracking().SingleAsync(x => x.Id == user.Id, cancellationToken);
        return Summary(row);
    }

    /// <summary>
    /// Sets another user's password, replacing any they have or giving one to a user who has none, and ends their
    /// sessions. Refused for the caller's own password, which they change on their account page. Not found when
    /// password sign-in is off.
    /// </summary>
    [HttpPut("{id:guid}/password")]
    [RequiresPasswordSignIn]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetPassword(Guid id, SetUserPasswordRequest request, CancellationToken cancellationToken)
    {
        var caller = await users.GetUserAsync(User);
        if (caller is null)
        {
            return Unauthorized();
        }

        if (caller.Id == id)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Change your own password on your account page.");
        }

        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        foreach (var validator in users.PasswordValidators)
        {
            var valid = await validator.ValidateAsync(users, user, request.Password);
            foreach (var error in valid.Errors)
            {
                ModelState.AddModelError(nameof(request.Password), error.Description);
            }
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        // The entry is saved by the same save as the password and the new security stamp, which ends the user's sessions.
        await audit.AddAsync(
            User,
            AuditActions.UserPasswordSet,
            new AuditSubject(AuditSubjectTypes.User, user.Id, user.ShownName),
            new { },
            cancellationToken: cancellationToken);

        user.PasswordHash = users.PasswordHasher.HashPassword(user, request.Password);
        var result = await users.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        return NoContent();
    }

    /// <summary>
    /// Gives another user a built-in role. Refused for the caller's own role, for a role single sign-on sets, and when
    /// it would leave no admin.
    /// </summary>
    [HttpPut("{id:guid}/role")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetRole(Guid id, SetUserRoleRequest request, CancellationToken cancellationToken)
    {
        // A missing role is caught by [Required], which leaves the role that cannot be given to a person, and a number
        // that names no role.
        if (request.Role is not { } role || role == BuiltInRole.Anonymous || !Enum.IsDefined(role))
        {
            ModelState.AddModelError(nameof(request.Role), "Choose admin, contributor or viewer.");
            return ValidationProblem(ModelState);
        }

        return Answer(await ChangeRoleOrDeleteAsync(id, role, cancellationToken));
    }

    /// <summary>
    /// Deletes another user. Their books stay with no owner and the audit entries they acted in stay. Refused for
    /// the caller's own account and when it would leave no admin.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        Answer(await ChangeRoleOrDeleteAsync(id, null, cancellationToken));

    private enum Outcome
    {
        Done,
        CallerGone,
        Own,
        NotFound,
        SingleSignOn,
        LastAdmin,
    }

    private IActionResult Answer(Outcome outcome) => outcome switch
    {
        Outcome.Done => NoContent(),
        Outcome.CallerGone => Unauthorized(),
        Outcome.NotFound => NotFound(),
        Outcome.Own => Problem(
            statusCode: StatusCodes.Status409Conflict, title: "You cannot change your own role or delete your own account."),
        Outcome.SingleSignOn => Problem(
            statusCode: StatusCodes.Status409Conflict, title: "Single sign-on sets this user's role."),
        _ => Problem(
            statusCode: StatusCodes.Status409Conflict, title: "Nook needs at least one admin."),
    };

    /// <summary>
    /// Gives a user a built-in role, or deletes them when none is given, with the audit entry stored by the same save.
    /// The database decides whether another admin remains, under a lock that makes requests at once run one after the
    /// other, because the caller's cookie may still say admin after they stopped being one.
    /// </summary>
    private async Task<Outcome> ChangeRoleOrDeleteAsync(Guid id, BuiltInRole? newRole, CancellationToken cancellationToken)
    {
        // Chosen once, outside the retry strategy, so a pass that runs again after a commit whose answer was lost finds
        // the entry the commit kept.
        var entryId = Guid.CreateVersion7();
        var callerId = users.GetUserId(User) is { } claimed && Guid.TryParse(claimed, out var parsed) ? parsed : (Guid?)null;
        if (callerId is null)
        {
            return Outcome.CallerGone;
        }

        // The database is set to retry, so the transaction has to run inside its retry strategy.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // A retry starts from what the database holds, not from what a failed attempt loaded.
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdminLockKey})", cancellationToken);

            // The caller is read first: an entry cannot name a user who is gone.
            var caller = await db.Users.SingleOrDefaultAsync(u => u.Id == callerId, cancellationToken);
            if (caller is null)
            {
                return Outcome.CallerGone;
            }

            if (await db.AuditEntries.AnyAsync(e => e.Id == entryId, cancellationToken))
            {
                return Outcome.Done;
            }

            if (caller.Id == id)
            {
                return Outcome.Own;
            }

            var target = await db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (target is null)
            {
                return Outcome.NotFound;
            }

            if (newRole is not null
                && oidc.Value.SetsRoles
                && await SingleSignOnLogins().AnyAsync(l => l.UserId == id, cancellationToken))
            {
                return Outcome.SingleSignOn;
            }

            var newRoleId = newRole is { } given ? Role.IdOf(given) : (Guid?)null;
            if (target.RoleId == Role.AdminId
                && newRoleId != Role.AdminId
                && !await db.Users.AnyAsync(u => u.Id != id && u.RoleId == Role.AdminId, cancellationToken))
            {
                return Outcome.LastAdmin;
            }

            // A request for the role the user already holds changes nothing, so the log is not told of a change.
            if (newRoleId == target.RoleId)
            {
                return Outcome.Done;
            }

            var before = await db.Roles.Where(r => r.Id == target.RoleId).Select(r => r.Name).SingleAsync(cancellationToken);
            var actor = new AuditActor(caller.Id, caller.ShownName, caller.ShownEmail);
            var subject = new AuditSubject(AuditSubjectTypes.User, target.Id, target.ShownName);

            if (newRoleId is { } roleId)
            {
                var after = await db.Roles.Where(r => r.Id == roleId).Select(r => r.Name).SingleAsync(cancellationToken);
                audit.Add(actor, AuditActions.UserRoleChanged, subject, new { before, after }, id: entryId);
                target.RoleId = roleId;
            }
            else
            {
                audit.Add(actor, AuditActions.UserDeleted, subject, new { email = target.ShownEmail, role = before }, id: entryId);
                db.Users.Remove(target);
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Outcome.Done;
        });
    }

    /// <summary>The key of the lock that makes a change that could leave no admin wait for the one before it.</summary>
    private const long AdminLockKey = 0x4E6F6F6B_00000002;

    /// <summary>The logins made through single sign-on, which are the ones whose user's role groups can set.</summary>
    private IQueryable<IdentityUserLogin<Guid>> SingleSignOnLogins() =>
        db.UserLogins.Where(l => l.LoginProvider == OidcSignIn.Scheme);

    /// <summary>Every user with what the list shows and orders by.</summary>
    private IQueryable<UserRow> Rows()
    {
        var singleSignOnLogins = SingleSignOnLogins();
        // The shown name is NookUser.ShownName, worked out in SQL; the id breaks ties, ascending whatever the direction, so a user never shows on two
        // pages or on none.
        return db.Users
            .AsNoTracking()
            .Join(db.Roles, u => u.RoleId, r => r.Id, (u, r) => new { User = u, Role = r.Name })
            .Select(x => new
            {
                x.User,
                x.Role,
                ShownEmail = x.User.Email ?? x.User.UserName ?? "",
            })
            .Select(x => new UserRow
            {
                Id = x.User.Id,
                CreatedAt = x.User.CreatedAt,
                Role = x.Role,
                RoleId = x.User.RoleId,
                HasOidcLogin = singleSignOnLogins.Any(l => l.UserId == x.User.Id),
                ShownEmail = x.ShownEmail,
                HasPassword = x.User.PasswordHash != null,
                HasSingleSignOn = db.UserLogins.Any(l => l.UserId == x.User.Id),
                HasAuditEntries = db.AuditEntries.Any(a => a.ActorId == x.User.Id),
                ShownName = !string.IsNullOrWhiteSpace(x.User.DisplayName)
                    ? x.User.DisplayName
                    : x.ShownEmail.IndexOf("@") > 0
                        ? x.ShownEmail.Substring(0, x.ShownEmail.IndexOf("@"))
                        : x.ShownEmail,
            });
    }

    private UserSummaryResponse Summary(UserRow x) =>
        new(x.Id, x.ShownName, x.ShownEmail, x.Role, x.CreatedAt, x.HasPassword, x.HasSingleSignOn, x.HasAuditEntries,
            Role.BuiltInOf(x.RoleId), oidc.Value.SetsRoles && x.HasOidcLogin);

    private sealed class UserRow
    {
        public Guid Id { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public required string Role { get; init; }
        public Guid RoleId { get; init; }
        public bool HasOidcLogin { get; init; }
        public required string ShownEmail { get; init; }
        public required string ShownName { get; init; }
        public bool HasPassword { get; init; }
        public bool HasSingleSignOn { get; init; }
        public bool HasAuditEntries { get; init; }
    }
}
