using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Nook.Api.Data;

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
                user = new NookUser { UserName = email, Email = email, EmailConfirmed = emailVerified };

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

                var linked = await users.AddLoginAsync(user, login);
                if (!linked.Succeeded)
                {
                    return Fail(Failed, "the provider account could not be linked to the user who has its email", linked);
                }
            }
        }

        var role = RoleFromGroups(principal, user.Role);
        if (role != user.Role)
        {
            user.Role = role;
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
    /// The role the provider's groups give: admin in the admin group, otherwise member in the member group,
    /// otherwise guest. With neither group named, the role stays as it is.
    /// </summary>
    private UserRole RoleFromGroups(ClaimsPrincipal principal, UserRole current)
    {
        var settings = options.Value;
        if (string.IsNullOrEmpty(settings.AdminGroup) && string.IsNullOrEmpty(settings.MemberGroup))
        {
            return current;
        }

        var groups = principal.FindAll(settings.GroupsClaim).Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(settings.AdminGroup) && groups.Contains(settings.AdminGroup))
        {
            return UserRole.Admin;
        }

        if (!string.IsNullOrEmpty(settings.MemberGroup) && groups.Contains(settings.MemberGroup))
        {
            return UserRole.Member;
        }

        return UserRole.Guest;
    }

    private string Fail(string code, string reason, IdentityResult? result = null)
    {
        var errors = result is null ? "" : string.Join(" ", result.Errors.Select(e => e.Description));
        logger.LogWarning("Single sign-on failed with {Code}: {Reason}. {Errors}", code, reason, errors);
        return code;
    }
}
