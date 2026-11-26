using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Nook.Api.Data;
using Nook.Api.Features.Security;
using static Nook.Api.Tests.Integration.PublicationManifests;

namespace Nook.Api.Tests.Integration;

/// <summary>
/// What a request from a proxy named in <c>Nook:TrustedProxies</c> is treated as: the scheme and client address it
/// forwards are taken, so the sign-in cookie and the links Nook builds follow the client's own https.
/// </summary>
public class TrustedProxyTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    private const string Peer = "172.18.0.3";
    private const string ForwardedClient = "203.0.113.10";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("172.18.0.3")]
    [InlineData("172.18.0.0/16")]
    [InlineData("172.18.0.9/16")]
    [InlineData("10.0.0.1, 172.18.0.0/16")]
    public async Task A_sign_in_through_a_trusted_proxy_over_https_sets_a_secure_cookie(string entry)
    {
        using var app = TrustingProxies(entry);
        var user = await app.CreateUserAsync();
        using var client = app.CreateClientFrom(Peer);

        var response = await SignInOverHttpsAsync(client, user.Email!);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("secure", response.SessionSetCookie(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("172.18.0.3")]
    [InlineData("172.18.0.0/16")]
    [InlineData("172.18.0.9/16")]
    [InlineData("10.0.0.1, 172.18.0.0/16")]
    public async Task A_manifest_through_a_trusted_proxy_over_https_links_over_https(string entry)
    {
        var part = await factory.IngestOnePartAsync(OneChapterBook($"Trusted {entry}"));
        using var app = TrustingProxies(entry);
        var user = await app.CreateUserAsync();
        using var client = app.CreateClientFrom(Peer);
        var login = await SignInOverHttpsAsync(client, user.Email!);

        var response = await GetManifestOverHttpsAsync(client, login, part.ItemId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("https://localhost/", SelfHref(await ReadManifestAsync(response)));
    }

    [Fact]
    public async Task A_proxy_seen_as_an_ipv4_mapped_address_is_trusted_by_its_ipv4_range()
    {
        using var app = TrustingProxies("172.18.0.0/16");
        var user = await app.CreateUserAsync();
        using var client = app.CreateClientFrom("::ffff:172.18.0.3");

        var response = await SignInOverHttpsAsync(client, user.Email!);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("secure", response.SessionSetCookie(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Behind_a_trusted_proxy_each_client_has_its_own_sign_in_limit()
    {
        using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(TrustedProxyOptions.Setting, Peer);
            builder.UseSetting("RateLimiting:Auth:PermitLimit", "2");
        });
        var user = await app.CreateUserAsync();
        using var client = app.CreateClientFrom(Peer);

        Assert.Equal(HttpStatusCode.Unauthorized, (await WrongPasswordSignInAsync(client, user.Email!, "203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WrongPasswordSignInAsync(client, user.Email!, "203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await WrongPasswordSignInAsync(client, user.Email!, "203.0.113.1")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await WrongPasswordSignInAsync(client, user.Email!, "203.0.113.2")).StatusCode);
    }

    [Fact]
    public async Task An_untrusted_proxys_forwarded_scheme_is_ignored()
    {
        var part = await factory.IngestOnePartAsync(OneChapterBook("Untrusted proxy"));
        using var app = TrustingProxies(Peer);
        var user = await app.CreateUserAsync();
        using var client = app.CreateClientFrom("198.51.100.7");

        var login = await SignInOverHttpsAsync(client, user.Email!);
        var response = await GetManifestOverHttpsAsync(client, login, part.ItemId);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.DoesNotContain("secure", login.SessionSetCookie(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("http://localhost/", SelfHref(await ReadManifestAsync(response)));
    }

    [Fact]
    public async Task A_forwarded_host_is_ignored_from_a_trusted_proxy()
    {
        var part = await factory.IngestOnePartAsync(OneChapterBook("Forwarded host"));
        using var app = TrustingProxies(Peer);
        var user = await app.CreateUserAsync();
        using var client = app.CreateClientFrom(Peer);

        var login = await SignInOverHttpsAsync(client, user.Email!);
        var response = await GetManifestOverHttpsAsync(client, login, part.ItemId, forwardedHost: "evil.example");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var self = SelfHref(await ReadManifestAsync(response));
        Assert.StartsWith("https://localhost/", self);
        Assert.DoesNotContain("evil.example", self);
    }

    [Fact]
    public async Task A_sign_in_over_plain_http_sets_a_cookie_that_is_not_secure()
    {
        var user = await factory.CreateUserAsync();
        using var client = factory.CreateClient();

        var response = await client.LoginAsync(user.Email!, ApiTestExtensions.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("secure", response.SessionSetCookie(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_untrusted_proxy_is_named_in_one_warning()
    {
        using var app = TrustingProxies(Peer);
        using var client = app.CreateClientFrom("198.51.100.7");

        for (var request = 0; request < 2; request++)
        {
            await GetMeWithForwardedProtoAsync(client);
        }

        var warnings = app.ReadLogs(LogLevel.Warning)
            .Where(message => message.Contains(TrustedProxyOptions.Setting, StringComparison.Ordinal))
            .ToList();
        var warning = Assert.Single(warnings);
        Assert.Contains("198.51.100.7", warning);
    }

    [Fact]
    public async Task An_untrusted_proxy_that_arrives_ipv4_mapped_is_named_by_its_ipv4_address()
    {
        using var app = TrustingProxies(Peer);
        using var client = app.CreateClientFrom("::ffff:198.51.100.7");

        await GetMeWithForwardedProtoAsync(client);

        var warning = Assert.Single(
            app.ReadLogs(LogLevel.Warning), message => message.Contains(TrustedProxyOptions.Setting, StringComparison.Ordinal));
        Assert.Contains("198.51.100.7", warning);
        Assert.DoesNotContain("::ffff:", warning);
    }

    [Fact]
    public async Task A_trusted_proxy_is_not_warned_about()
    {
        using var app = TrustingProxies(Peer);
        using var client = app.CreateClientFrom(Peer);

        await GetMeWithForwardedProtoAsync(client);
        await GetMeWithForwardedProtoAsync(client, forwardedFor: "unknown");

        Assert.DoesNotContain(
            app.ReadLogs(LogLevel.Warning), message => message.Contains(TrustedProxyOptions.Setting, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("proxy.local")]
    [InlineData("172.18")]
    [InlineData("172.18/16")]
    [InlineData("010.0.0.5")]
    [InlineData("0x0a.0.0.5")]
    [InlineData("010.0.0.0/8")]
    public void Api_refuses_to_start_with_a_trusted_proxy_that_is_not_an_address_or_range(string badEntry)
    {
        using var app = TrustingProxies($"{Peer}, {badEntry}");

        var exception = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        var messages = exception.Flatten().Select(inner => inner.Message).ToList();
        Assert.Contains(messages, message =>
            message.Contains(TrustedProxyOptions.Setting, StringComparison.Ordinal)
            && message.Contains(badEntry, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_forwarded_headers_variable_trusts_any_proxy_and_consumes_one_address()
    {
        using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ForwardedHeaders_Enabled", "true");
            builder.UseSetting("RateLimiting:Auth:PermitLimit", "1");
        });
        var user = await app.CreateUserAsync();
        using var client = app.CreateClientFrom("198.51.100.7");

        var secure = await SignInOverHttpsAsync(client, user.Email!);
        var first = await WrongPasswordSignInAsync(client, user.Email!, "203.0.113.1, 203.0.113.9");
        var second = await WrongPasswordSignInAsync(client, user.Email!, "203.0.113.2, 203.0.113.9");

        Assert.Contains("secure", secure.SessionSetCookie(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Contains(
            app.ReadLogs(LogLevel.Warning),
            message => message.Contains("ASPNETCORE_FORWARDEDHEADERS_ENABLED", StringComparison.Ordinal)
                && message.Contains(TrustedProxyOptions.Setting, StringComparison.Ordinal));
    }

    private static Task<HttpResponseMessage> GetMeWithForwardedProtoAsync(HttpClient client, string? forwardedFor = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("X-Forwarded-Proto", "https");
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return client.SendAsync(request, CancellationToken);
    }

    private WebApplicationFactory<Program> TrustingProxies(string entry) =>
        factory.WithWebHostBuilder(builder => builder.UseSetting(TrustedProxyOptions.Setting, entry));

    private static Task<HttpResponseMessage> WrongPasswordSignInAsync(HttpClient client, string email, string forwardedFor) =>
        SignInAsync(client, email, "not-the-password", forwardedFor);

    private static Task<HttpResponseMessage> SignInOverHttpsAsync(HttpClient client, string email) =>
        SignInAsync(client, email, ApiTestExtensions.Password, ForwardedClient, forwardedProto: "https");

    private static Task<HttpResponseMessage> SignInAsync(
        HttpClient client, string email, string password, string forwardedFor, string? forwardedProto = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password }),
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        if (forwardedProto is not null)
        {
            request.Headers.Add("X-Forwarded-Proto", forwardedProto);
        }

        return client.SendAsync(request, CancellationToken);
    }

    /// <summary>Asks for a book's manifest as the signed-in user, with the headers a proxy that ends https sends.</summary>
    private static Task<HttpResponseMessage> GetManifestOverHttpsAsync(
        HttpClient client, HttpResponseMessage login, Guid itemId, string? forwardedHost = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ManifestUrl(itemId));
        request.Headers.Add("Cookie", login.SessionCookiePair());
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", ForwardedClient);
        if (forwardedHost is not null)
        {
            request.Headers.Add("X-Forwarded-Host", forwardedHost);
        }

        return client.SendAsync(request, CancellationToken);
    }
}
