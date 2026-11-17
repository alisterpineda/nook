using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features.PublicAccess;

namespace Nook.Api.Tests.Integration;

public class InstanceSettingsTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Public_access_is_off_until_turned_on_and_stays_as_last_saved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.False(await ReadEnabledAsync(cancellationToken));

        await SetEnabledAsync(true, cancellationToken);
        Assert.True(await ReadEnabledAsync(cancellationToken));

        await SetEnabledAsync(false, cancellationToken);
        Assert.False(await ReadEnabledAsync(cancellationToken));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        Assert.Equal(1, await db.Settings.CountAsync(cancellationToken));
    }

    private async Task<bool> ReadEnabledAsync(CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<InstanceSettings>().PublicAccessEnabledAsync(cancellationToken);
    }

    private async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InstanceSettings>().SetPublicAccessEnabledAsync(enabled, cancellationToken);
    }
}
