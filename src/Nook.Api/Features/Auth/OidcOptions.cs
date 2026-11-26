namespace Nook.Api.Features.Auth;

/// <summary>
/// Single sign-on through an OpenID Connect provider, bound from <c>Nook:Oidc</c>.
/// It is on when <see cref="Authority"/> is set.
/// </summary>
public sealed class OidcOptions
{
    public const string Section = "Nook:Oidc";

    /// <summary>The provider's issuer URL. Setting it turns single sign-on on.</summary>
    public string? Authority { get; set; }

    /// <summary>The client id Nook is registered under at the provider. Required when single sign-on is on.</summary>
    public string? ClientId { get; set; }

    /// <summary>The client secret Nook is registered with at the provider. Required when single sign-on is on.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>The provider's name as the sign-in page shows it.</summary>
    public string DisplayName { get; set; } = "SSO";

    /// <summary>The group whose members are admins. Unset, it matches nobody.</summary>
    public string? AdminGroup { get; set; }

    /// <summary>The group whose members are contributors. Unset, it matches nobody.</summary>
    public string? ContributorGroup { get; set; }

    /// <summary>The claim that lists the groups someone is in.</summary>
    public string GroupsClaim { get; set; } = "groups";

    /// <summary>The scopes asked of the provider, separated by spaces.</summary>
    public string Scopes { get; set; } = "openid profile email";

    /// <summary>Whether single sign-on is on.</summary>
    public bool Enabled => !string.IsNullOrWhiteSpace(Authority);

    /// <summary>Whether single sign-on sets roles from groups: it is on and the admin or contributor group is named.</summary>
    public bool SetsRoles => Enabled && (!string.IsNullOrEmpty(AdminGroup) || !string.IsNullOrEmpty(ContributorGroup));

    /// <summary>Whether the settings can be used: when single sign-on is on, the client id and secret are set.</summary>
    public bool IsValid() => !Enabled || (!string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret));
}
