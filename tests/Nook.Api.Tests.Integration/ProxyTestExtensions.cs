using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nook.Api.Tests.Integration;

/// <summary>Reaching the API as a client at a chosen address, as a reverse proxy would.</summary>
internal static class ProxyTestExtensions
{
    /// <summary>
    /// A client whose requests arrive from <paramref name="peer"/>, since the in-memory server gives a request no
    /// address. It keeps no cookies: a test sends the session cookie itself.
    /// </summary>
    public static HttpClient CreateClientFrom(this WebApplicationFactory<Program> app, string peer) =>
        new(app.Server.CreateHandler(context => context.Connection.RemoteIpAddress = IPAddress.Parse(peer)))
        {
            BaseAddress = new Uri("http://localhost"),
        };
}
