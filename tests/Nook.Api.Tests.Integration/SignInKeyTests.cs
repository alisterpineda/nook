using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Nook.Api.Data;

namespace Nook.Api.Tests.Integration;

public class SignInKeyTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task A_sign_in_is_still_accepted_after_the_api_restarts_over_the_same_data_folder()
    {
        var user = await factory.CreateUserAsync(UserRole.Member);
        using var client = factory.CreateClient();
        var login = await client.LoginAsync(user.Email!, ApiTestExtensions.Password);
        login.EnsureSuccessStatusCode();
        var sessionCookie = login.SessionCookiePair();

        // A second host over the same data folder and database stands for the API after a restart.
        await using var restarted = factory.WithWebHostBuilder(_ => { });
        using var restartedClient = restarted.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("Cookie", sessionCookie);

        var response = await restartedClient.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var keysFolder = Path.Combine(factory.DataPath, "keys", "data-protection");
        Assert.True(
            Directory.Exists(keysFolder) && Directory.EnumerateFiles(keysFolder, "*.xml").Any(),
            $"No sign-in cookie key was kept in {keysFolder}.");
    }
}
