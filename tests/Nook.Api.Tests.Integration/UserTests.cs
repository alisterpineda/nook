using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Npgsql;

namespace Nook.Api.Tests.Integration;

public class UserTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task New_user_gets_a_version_7_id()
    {
        var user = await CreateUserAsync(new NookUser { UserName = "v7" });

        var saved = await FindAsync(user.Id);

        Assert.Equal(7, saved.Id.Version);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Member)]
    [InlineData(UserRole.Guest)]
    public async Task Role_round_trips_for_each_value(UserRole role)
    {
        var user = await CreateUserAsync(new NookUser { UserName = $"role-{role}", Role = role });

        var saved = await FindAsync(user.Id);
        var stored = await ReadRoleColumnAsync(user.Id);

        Assert.Equal(role, saved.Role);
        Assert.Equal(role.ToString(), stored);
    }

    [Fact]
    public async Task User_without_password_can_hold_an_external_login()
    {
        var user = await CreateUserAsync(new NookUser { UserName = "oidc" });
        var login = new UserLoginInfo("pocket-id", "2f6c1a7e-sub", "Pocket ID");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<NookUser>>();
            var tracked = await users.FindByIdAsync(user.Id.ToString());
            AssertSucceeded(await users.AddLoginAsync(tracked!, login));
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<NookUser>>();
            var found = await users.FindByLoginAsync("pocket-id", "2f6c1a7e-sub");

            Assert.NotNull(found);
            Assert.Equal(user.Id, found.Id);
            Assert.Null(found.PasswordHash);
        }
    }

    [Fact]
    public async Task Timestamps_are_set_on_create_and_updated_on_change()
    {
        var before = DateTimeOffset.UtcNow;
        var user = await CreateUserAsync(new NookUser { UserName = "stamped" });
        var created = await FindAsync(user.Id);

        Assert.Equal(TimeSpan.Zero, created.CreatedAt.Offset);
        Assert.InRange(created.CreatedAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(created.CreatedAt, created.UpdatedAt);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<NookUser>>();
            var tracked = await users.FindByIdAsync(user.Id.ToString());
            tracked!.Role = UserRole.Member;
            AssertSucceeded(await users.UpdateAsync(tracked));
        }

        var changed = await FindAsync(user.Id);

        Assert.Equal(created.CreatedAt, changed.CreatedAt);
        Assert.True(changed.UpdatedAt > created.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, changed.UpdatedAt.Offset);
    }

    private async Task<NookUser> CreateUserAsync(NookUser user)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<NookUser>>();
        AssertSucceeded(await users.CreateAsync(user));
        return user;
    }

    private async Task<NookUser> FindAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<NookUser>>();
        var user = await users.FindByIdAsync(id.ToString());
        Assert.NotNull(user);
        return user;
    }

    private async Task<string?> ReadRoleColumnAsync(Guid id)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT role FROM asp_net_users WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    private static void AssertSucceeded(IdentityResult result) =>
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
}
