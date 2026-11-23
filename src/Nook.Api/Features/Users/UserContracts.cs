using System.ComponentModel.DataAnnotations;

namespace Nook.Api.Features.Users;

public class UserListQuery
{
    [Range(0, int.MaxValue)]
    public int Offset { get; set; }

    [Range(1, 200)]
    public int Limit { get; set; } = 50;

    /// <summary>What the list is ordered by.</summary>
    public UserSort Sort { get; set; } = UserSort.Name;

    /// <summary>Which way <see cref="Sort"/> runs. Users whose values are equal always come in id order.</summary>
    public SortDirection Direction { get; set; } = SortDirection.Asc;
}

/// <summary>What the list of users is ordered by.</summary>
public enum UserSort
{
    Name,
    Email,
    Role,
    Joined,
}

/// <summary>Which way a list runs.</summary>
public enum SortDirection
{
    Asc,
    Desc,
}

/// <summary>One user in the list: the name and email as shown, the name of their role, when they joined, whether a password is stored and a single sign-on login is linked, and whether the audit log holds an entry they acted in.</summary>
public record UserSummaryResponse(Guid Id, string Name, string Email, string Role, DateTimeOffset JoinedAt,
    bool HasPassword, bool HasSingleSignOn, bool HasAuditEntries);
