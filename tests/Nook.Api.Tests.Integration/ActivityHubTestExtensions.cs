using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features.Activity;
using Nook.Api.Features.Jobs;

namespace Nook.Api.Tests.Integration;

/// <summary>A connection to the activity hub that keeps what it is sent, so a test can wait for the next message of a kind.</summary>
internal sealed class ActivityHubConnection : IAsyncDisposable
{
    /// <summary>How long to wait for a message that should come.</summary>
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    private readonly Channel<(string Method, object Payload)> messages = Channel.CreateUnbounded<(string, object)>();
    private readonly List<(string Method, object Payload)> unread = [];

    public ActivityHubConnection(HubConnection connection, Guid userId, HttpClient client)
    {
        Connection = connection;
        UserId = userId;
        Client = client;

        connection.On<UploadActivityResponse>("UploadChanged", m => Add("UploadChanged", m));
        connection.On<Guid>("UploadRemoved", m => Add("UploadRemoved", m));
        connection.On<JobSummaryResponse>("JobChanged", m => Add("JobChanged", m));
        connection.On<Guid>("JobRemoved", m => Add("JobRemoved", m));
    }

    public HubConnection Connection { get; }

    /// <summary>The id of the user the connection signed in as.</summary>
    public Guid UserId { get; }

    /// <summary>The client the user signed in with, to act as them.</summary>
    public HttpClient Client { get; }

    private void Add(string method, object payload) => messages.Writer.TryWrite((method, payload));

    /// <summary>The next message of a kind, skipping others. Fails the test when none arrives in time.</summary>
    public async Task<T> NextAsync<T>(string method, Func<T, bool>? where = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Wait);
        try
        {
            while (true)
            {
                var (kind, payload) = await messages.Reader.ReadAsync(timeout.Token);
                if (kind == method && payload is T typed && (where?.Invoke(typed) ?? true))
                {
                    return typed;
                }
            }
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"No {method} message arrived within {Wait.TotalSeconds} seconds.");
        }
    }

    /// <summary>Asserts that no message of a kind arrives within a short while.</summary>
    public async Task AssertNoneAsync<T>(string method, Func<T, bool>? where = null, TimeSpan? within = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(within ?? TimeSpan.FromSeconds(1.5));
        try
        {
            while (true)
            {
                var (kind, payload) = await messages.Reader.ReadAsync(timeout.Token);
                Assert.False(
                    kind == method && payload is T typed && (where?.Invoke(typed) ?? true),
                    $"A {method} message arrived that should not have.");
            }
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync();
        Client.Dispose();
    }
}

internal static class ActivityHubTestExtensions
{
    /// <summary>The hub's address.</summary>
    public const string HubPath = "/api/hubs/activity";

    /// <summary>Creates a user, signs them in and connects them to the activity hub, started.</summary>
    public static Task<ActivityHubConnection> ConnectToActivityHubAsync(
        this WebApplicationFactory<Program> factory, BuiltInRole role = BuiltInRole.Contributor) =>
        factory.ConnectToActivityHubAsync(Role.IdOf(role));

    /// <summary>Creates a user in any role, signs them in and connects them to the activity hub, started.</summary>
    public static async Task<ActivityHubConnection> ConnectToActivityHubAsync(
        this WebApplicationFactory<Program> factory, Guid roleId)
    {
        var user = await factory.CreateUserAsync(roleId);
        var client = factory.CreateClient();
        var login = await client.LoginAsync(user.Email!, ApiTestExtensions.Password);
        login.EnsureSuccessStatusCode();

        var connection = factory.BuildActivityHubConnection(login.SessionCookiePair());
        var hub = new ActivityHubConnection(connection, user.Id, client);
        await connection.StartAsync(TestContext.Current.CancellationToken);
        return hub;
    }

    /// <summary>A connection to the activity hub that sends a <c>Cookie</c> header, or none, and is not yet started.</summary>
    public static HubConnection BuildActivityHubConnection(this WebApplicationFactory<Program> factory, string? cookie = null) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, HubPath), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (cookie is not null)
                {
                    options.Headers["Cookie"] = cookie;
                }
            })
            .AddJsonProtocol(options =>
            {
                // Enums travel by name, as the hub writes them.
                options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            })
            .Build();
}
