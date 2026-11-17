using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;

namespace Nook.Api.Features.PublicAccess;

/// <summary>The settings an admin changes in the app, read from and saved to the settings table.</summary>
public class InstanceSettings(NookDbContext db)
{
    internal const string PublicAccessEnabledKey = "public_access.enabled";

    /// <summary>Whether a visitor with no account may use what the Anonymous role allows. Off until an admin turns it on.</summary>
    public async Task<bool> PublicAccessEnabledAsync(CancellationToken cancellationToken = default)
    {
        var value = await db.Settings
            .Where(s => s.Key == PublicAccessEnabledKey)
            .Select(s => s.Value)
            .SingleOrDefaultAsync(cancellationToken);
        return bool.TryParse(value, out var enabled) && enabled;
    }

    /// <summary>Turns public access on or off and saves it.</summary>
    public async Task SetPublicAccessEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var value = enabled ? bool.TrueString : bool.FalseString;
        var setting = await db.Settings.SingleOrDefaultAsync(s => s.Key == PublicAccessEnabledKey, cancellationToken);
        if (setting is null)
        {
            db.Settings.Add(new Setting { Key = PublicAccessEnabledKey, Value = value });
        }
        else
        {
            setting.Value = value;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
