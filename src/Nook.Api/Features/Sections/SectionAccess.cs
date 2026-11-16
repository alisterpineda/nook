using System.Security.Claims;
using Nook.Api.Data;

namespace Nook.Api.Features.Sections;

/// <summary>
/// The one place that decides which sections, and so which works, a user can see.
/// Every query that returns catalog data to a user goes through it.
/// </summary>
public static class SectionAccess
{
    // Every signed-in user sees every section until users are granted sections one by one.
    public static IQueryable<LibrarySection> VisibleTo(this IQueryable<LibrarySection> sections, ClaimsPrincipal user) =>
        sections;

    public static IQueryable<Work> VisibleTo(this IQueryable<Work> works, ClaimsPrincipal user) => works;
}
