using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Nook.Api.Data;
using Nook.Api.Features.Setup;

namespace Nook.Api.Features.Auth;

/// <summary>
/// Single sign-on through an OpenID Connect provider: finds or creates the Nook user for someone the provider
/// has signed in, and signs them in to Nook.
/// </summary>
public sealed class OidcSignIn(
    UserManager<NookUser> users,
    SignInManager<NookUser> signIn,
    NookDbContext db,
    IOptions<OidcOptions> options,
    ILogger<OidcSignIn> logger)
{
    /// <summary>
    /// The name of the OpenID Connect authentication scheme, which is also the login provider name stored for
    /// a user who signs in through it.
    /// </summary>
    public const string Scheme = "oidc";

    /// <summary>Single sign-on did not work, for a reason the user cannot act on.</summary>
    public const string Failed = "sso-failed";

    /// <summary>The provider sent no email for someone Nook has not seen.</summary>
    public const string NoEmail = "sso-no-email";

    /// <summary>A Nook user has the provider's email, and the provider has not confirmed the email is theirs.</summary>
    public const string EmailUnverified = "sso-email-unverified";

    /// <summary>Nook has no users yet, and the person signing in would not be an admin.</summary>
    public const string SetupRequired = "sso-setup-required";

    /// <summary>
    /// Finds the Nook user for the person the provider signed in, creating or linking one when needed, sets their
    /// role from their groups and signs them in to Nook.
    /// </summary>
    /// <param name="principal">The claims the provider sent, under the names the provider gave them.</param>
    /// <returns>Null when the user is signed in, otherwise the code of the failure.</returns>
    public async Task<string?> SignInAsync(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue("sub");
        if (string.IsNullOrEmpty(subject))
        {
            return Fail(Failed, "the provider sent no subject");
        }

        var user = await users.FindByLoginAsync(Scheme, subject);
        if (user is null)
        {
            var email = principal.FindFirstValue("email");
            if (string.IsNullOrEmpty(email))
            {
                return Fail(NoEmail, "the provider sent no email for someone Nook has not seen");
            }

            var emailVerified = string.Equals(principal.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
            var login = new UserLoginInfo(Scheme, subject, Scheme);

            user = await users.FindByNameAsync(email);
            if (user is null)
            {
                // First-run setup closes for good once any user exists. Someone who is not an admin must not be
                // the first user, or Nook would be left with no admin and no way to create one.
                if (RoleFromGroups(principal, BuiltInRole.Viewer) != BuiltInRole.Admin && await FirstRunSetup.IsRequiredAsync(db))
                {
                    return Fail(SetupRequired, "Nook has no users yet and the provider's groups do not make this person an admin");
                }

                // The role is set here, not only by the save below, so a first user who is an admin is never stored
                // as anything less.
                user = new NookUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = emailVerified,
                    RoleId = Role.IdOf(RoleFromGroups(principal, BuiltInRole.Viewer)),
                };

                // The login is added before the user is created, so the one save that creates the user writes
                // both, and a failure leaves neither.
                db.UserLogins.Add(new IdentityUserLogin<Guid>
                {
                    LoginProvider = login.LoginProvider,
                    ProviderKey = login.ProviderKey,
                    ProviderDisplayName = login.ProviderDisplayName,
                    UserId = user.Id,
                });
                var created = await users.CreateAsync(user);
                if (!created.Succeeded)
                {
                    return Fail(Failed, "the user could not be created", created);
                }
            }
            else
            {
                // Someone who controls an address at the provider without proving it could otherwise take over
                // the Nook user who has it.
                if (!emailVerified)
                {
                    return Fail(EmailUnverified, "a Nook user has the email and the provider has not verified it");
                }

                // Nook signs in through one provider, so a user who already has a login there is someone else's
                // account, perhaps one created from this address before the provider had verified whose it is.
                if ((await users.GetLoginsAsync(user)).Any(existing => existing.LoginProvider == Scheme))
                {
                    return Fail(Failed, "the Nook user who has the email is already linked to another provider account");
                }

                var linked = await users.AddLoginAsync(user, login);
                if (!linked.Succeeded)
                {
                    return Fail(Failed, "the provider account could not be linked to the user who has its email", linked);
                }
            }
        }

        var roleId = Role.IdOf(RoleFromGroups(principal, user.BuiltInRoleOrViewer));
        if (roleId != user.RoleId)
        {
            user.RoleId = roleId;
            var updated = await users.UpdateAsync(user);
            if (!updated.Succeeded)
            {
                return Fail(Failed, "the user's role could not be saved", updated);
            }
        }

        await signIn.SignInAsync(user, isPersistent: true);
        return null;
    }

    /// <summary>
    /// The role the provider's groups give: admin in the admin group, otherwise contributor in the contributor group,
    /// otherwise viewer. With neither group named, the role stays as it is.
    /// </summary>
    private BuiltInRole RoleFromGroups(ClaimsPrincipal principal, BuiltInRole current)
    {
        var settings = options.Value;
        if (string.IsNullOrEmpty(settings.AdminGroup) && string.IsNullOrEmpty(settings.ContributorGroup))
        {
            return current;
        }

        var groups = principal.FindAll(settings.GroupsClaim).Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(settings.AdminGroup) && groups.Contains(settings.AdminGroup))
        {
            return BuiltInRole.Admin;
        }

        if (!string.IsNullOrEmpty(settings.ContributorGroup) && groups.Contains(settings.ContributorGroup))
        {
            return BuiltInRole.Contributor;
        }

        return BuiltInRole.Viewer;
    }

    private string Fail(string code, string reason, IdentityResult? result = null)
    {
        var errors = result is null ? "" : string.Join(" ", result.Errors.Select(e => e.Description));
        logger.LogWarning("Single sign-on failed with {Code}: {Reason}. {Errors}", code, reason, errors);
        return code;
    }
}
