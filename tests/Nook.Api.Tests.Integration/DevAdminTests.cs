extern alias AppHost;

using System.Net;
using AppHost::Nook.AppHost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Nook.Api.Data;
using Nook.Api.Features.Auth;
using Nook.Api.Features.Setup;

namespace Nook.Api.Tests.Integration;

public class DevAdminTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    // One test, because setup can only happen once per database and tests in a class share theirs.
    [Fact]
    public async Task The_dev_admin_is_created_on_an_empty_database_and_can_sign_in()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Assert.Equal("admin@nook.test", DevAdmin.Email);
        Assert.Equal("admin@nook.test", DevAdmin.Password);

        var logger = await factory.EnsureDevAdminAsync(cancellationToken);

        using var client = factory.CreateClient();
        Assert.False((await client.GetAsync<SetupStatusResponse>("/api/setup")).Required);

        var login = await client.LoginAsync(DevAdmin.Email, DevAdmin.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(UserRole.Admin, (await login.ReadAsync<CurrentUserResponse>()).Role);

        Assert.Single(logger.Collector.GetSnapshot(), r =>
            r.Level == LogLevel.Information && r.Message.Contains(DevAdmin.Email) && r.Message.Contains(DevAdmin.Password));

        // Starting the stack again finds Nook already set up.
        var againLogger = await factory.EnsureDevAdminAsync(cancellationToken);

        using var later = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await later.LoginAsync(DevAdmin.Email, DevAdmin.Password)).StatusCode);
        Assert.Single(againLogger.Collector.GetSnapshot(), r => r.Level == LogLevel.Information);
        Assert.DoesNotContain(againLogger.Collector.GetSnapshot(), r => r.Message.Contains(DevAdmin.Password));
    }
}

public class DevAdminOnExistingInstallTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task An_install_that_is_already_set_up_gets_no_dev_admin()
    {
        await factory.CreateUserAsync(UserRole.Admin);

        var logger = await factory.EnsureDevAdminAsync(TestContext.Current.CancellationToken);

        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync(DevAdmin.Email, DevAdmin.Password)).StatusCode);
        Assert.Single(logger.Collector.GetSnapshot(), r => r.Level == LogLevel.Information);
        Assert.DoesNotContain(logger.Collector.GetSnapshot(), r => r.Level == LogLevel.Warning);
        Assert.DoesNotContain(logger.Collector.GetSnapshot(), r => r.Message.Contains(DevAdmin.Password));
    }
}

public class DevAdminFailureTests
{
    [Fact]
    public async Task An_api_that_cannot_be_reached_is_logged_as_a_warning()
    {
        var logger = await EnsureAgainstAsync(new ThrowingHandler(new HttpRequestException("Connection refused.")));

        AssertOneWarningWithoutThePassword(logger);
    }

    [Fact]
    public async Task An_api_that_refuses_setup_is_logged_as_a_warning()
    {
        var logger = await EnsureAgainstAsync(new StatusHandler(HttpStatusCode.InternalServerError));

        AssertOneWarningWithoutThePassword(logger);
    }

    [Fact]
    public async Task An_api_that_times_out_is_logged_as_a_warning()
    {
        var logger = await EnsureAgainstAsync(new ThrowingHandler(new TaskCanceledException("The request timed out.")));

        AssertOneWarningWithoutThePassword(logger);
    }

    private static async Task<FakeLogger> EnsureAgainstAsync(HttpMessageHandler handler)
    {
        var logger = new FakeLogger();
        using var api = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };

        await DevAdmin.EnsureAsync(api, logger, CancellationToken.None);

        return logger;
    }

    private static void AssertOneWarningWithoutThePassword(FakeLogger logger)
    {
        Assert.Single(logger.Collector.GetSnapshot(), r => r.Level == LogLevel.Warning);
        Assert.DoesNotContain(logger.Collector.GetSnapshot(), r => r.Message.Contains(DevAdmin.Password));
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }
}

file static class DevAdminFactoryExtensions
{
    /// <summary>Ensures the dev admin against the factory's API and returns what was logged.</summary>
    public static async Task<FakeLogger> EnsureDevAdminAsync(this NookApiFactory factory, CancellationToken cancellationToken)
    {
        var logger = new FakeLogger();
        using var api = factory.CreateClient();

        await DevAdmin.EnsureAsync(api, logger, cancellationToken);

        return logger;
    }
}
