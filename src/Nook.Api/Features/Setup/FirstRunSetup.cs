using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;

namespace Nook.Api.Features.Setup;

/// <summary>When first-run setup is open, for everything that depends on it.</summary>
public static class FirstRunSetup
{
    /// <summary>Whether Nook still needs its first admin: it has no users yet.</summary>
    public static async Task<bool> IsRequiredAsync(NookDbContext db, CancellationToken cancellationToken = default) =>
        !await db.Users.AnyAsync(cancellationToken);
}
