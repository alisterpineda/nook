using System.Security.Claims;
using Nook.Api.Data;

namespace Nook.Api.Features.Libraries;

/// <summary>
/// The one place that decides which libraries, and so which works, a user can see.
/// Every query that returns catalog data to a user goes through it.
/// </summary>
public static class LibraryAccess
{
    // Every signed-in user sees every library until users are granted libraries one by one.
    public static IQueryable<Library> VisibleTo(this IQueryable<Library> libraries, ClaimsPrincipal user) => libraries;

    public static IQueryable<Work> VisibleTo(this IQueryable<Work> works, ClaimsPrincipal user) => works;
}
