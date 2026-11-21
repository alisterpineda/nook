namespace Nook.Api.Features.Auth;

/// <summary>
/// Whether password sign-in and first-run setup are available, set by <c>Nook:PasswordSignIn</c>. It is on unless the
/// setting is <c>false</c>.
/// </summary>
public sealed class PasswordSignInOptions
{
    public const string Setting = "Nook:PasswordSignIn";

    /// <summary>Whether people can sign in with an email and password.</summary>
    public bool Enabled { get; set; } = true;
}
