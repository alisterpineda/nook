namespace Nook.Api.Data;

/// <summary>
/// The roles Nook ships with, each a row in the roles table under a fixed id. The name is what the session's role
/// claim carries.
/// </summary>
public enum BuiltInRole
{
    Admin,
    Contributor,
    Viewer,
    Anonymous,
}
