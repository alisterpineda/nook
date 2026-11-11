namespace Nook.Api.Data;

/// <summary>What a user may do in Nook. Stored as the member name, so new members need no migration.</summary>
public enum UserRole
{
    Admin,
    Member,
    Guest,
}
