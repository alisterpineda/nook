using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Features.Audit;
using Nook.Api.Features.Auth;

namespace Nook.Api.Features.Users;

/// <summary>The people who have an account, for admins to read, add and give passwords to.</summary>
[ApiController]
[Route("api/users")]
[Authorize(Roles = nameof(BuiltInRole.Admin))]
public class UsersController(NookDbContext db, UserManager<NookUser> users, AuditLog audit) : ControllerBase
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
        // A missing role is caught by [Required], which leaves only the role that cannot be given to a person.
        if (request.Role is not { } role || role == BuiltInRole.Anonymous)
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

    /// <summary>Every user with what the list shows and orders by.</summary>
    private IQueryable<UserRow> Rows()
    {
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

    private static UserSummaryResponse Summary(UserRow x) =>
        new(x.Id, x.ShownName, x.ShownEmail, x.Role, x.CreatedAt, x.HasPassword, x.HasSingleSignOn, x.HasAuditEntries);

    private sealed class UserRow
    {
        public Guid Id { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public required string Role { get; init; }
        public required string ShownEmail { get; init; }
        public required string ShownName { get; init; }
        public bool HasPassword { get; init; }
        public bool HasSingleSignOn { get; init; }
        public bool HasAuditEntries { get; init; }
    }
}
