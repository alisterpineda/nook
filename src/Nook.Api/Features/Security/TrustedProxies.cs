using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using IPNetwork = System.Net.IPNetwork;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Nook.Api.Features.Security;

/// <summary>
/// The proxies in front of Nook whose forwarded scheme and client address it takes, from <c>Nook:TrustedProxies</c>.
/// Without any, only the framework's default trust of loopback applies.
/// </summary>
public sealed class TrustedProxyOptions
{
    public const string Setting = "Nook:TrustedProxies";

    /// <summary>Addresses and ranges such as <c>172.18.0.0/16</c>, separated by commas or whitespace.</summary>
    public string? Entries { get; set; }

    /// <summary>Splits <see cref="Entries"/> into the addresses, the ranges and the entries that are neither.</summary>
    public (List<IPAddress> Addresses, List<IPNetwork> Ranges, List<string> Unreadable) Parse()
    {
        var addresses = new List<IPAddress>();
        var ranges = new List<IPNetwork>();
        var unreadable = new List<string>();

        var entries = (Entries ?? "").Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in entries)
        {
            if (TryParseRange(entry, out var range))
            {
                ranges.Add(range);
            }
            else if (!entry.Contains('/') && TryParseAddress(entry, out var address))
            {
                addresses.Add(address);
            }
            else
            {
                unreadable.Add(entry);
            }
        }

        return (addresses, ranges, unreadable);
    }

    // IPAddress.TryParse takes shorthand such as 172.18, as 172.0.0.18, and reads 010 as octal, so an IPv4 entry must
    // be written the way the address it parses to is.
    private static bool TryParseAddress(string text, out IPAddress address)
    {
        if (IPAddress.TryParse(text, out var parsed)
            && (text.Contains(':') || parsed.ToString() == text))
        {
            address = parsed;
            return true;
        }

        address = IPAddress.None;
        return false;
    }

    private static bool TryParseRange(string text, out IPNetwork range)
    {
        range = default;
        var slash = text.IndexOf('/');
        if (slash < 0 || !TryParseAddress(text[..slash], out var address))
        {
            return false;
        }

        // IPNetwork refuses an address with bits set past the prefix, so such an entry is cut to its prefix here.
        var prefixText = text[(slash + 1)..];
        var bits = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (prefixText.Length == 0 || !prefixText.All(char.IsAsciiDigit)
            || !int.TryParse(prefixText, out var prefix) || prefix > bits)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        for (var bit = prefix; bit < bits; bit++)
        {
            bytes[bit / 8] &= (byte)~(0x80 >> (bit % 8));
        }

        range = new IPNetwork(new IPAddress(bytes), prefix);
        return true;
    }
}

/// <summary>Refuses a <c>Nook:TrustedProxies</c> entry that is neither an address nor a range, which would otherwise leave a proxy untrusted without a word.</summary>
public sealed class TrustedProxyOptionsValidator : IValidateOptions<TrustedProxyOptions>
{
    public ValidateOptionsResult Validate(string? name, TrustedProxyOptions options)
    {
        var unreadable = options.Parse().Unreadable;
        return unreadable.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(unreadable.Select(entry =>
                $"{TrustedProxyOptions.Setting} has `{entry}`, which is neither an IP address nor a range such as 172.18.0.0/16."));
    }
}

public static partial class TrustedProxies
{
    /// <summary>
    /// Reads <c>Nook:TrustedProxies</c> and adds what it names to the proxies the forwarded headers middleware trusts.
    /// The framework's own variable empties those lists, so trust comes from this setting instead.
    /// </summary>
    public static IServiceCollection AddTrustedProxies(this IServiceCollection services)
    {
        services.AddOptions<TrustedProxyOptions>()
            .Configure<IConfiguration>((options, configuration) => options.Entries = configuration[TrustedProxyOptions.Setting])
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TrustedProxyOptions>, TrustedProxyOptionsValidator>();

        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<TrustedProxyOptions>>((options, trusted) =>
            {
                // The proxy must keep Host as the client sent it: only the scheme and the client address are taken.
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

                var (addresses, ranges, _) = trusted.Value.Parse();
                foreach (var address in addresses)
                {
                    options.KnownProxies.Add(address);
                }

                foreach (var range in ranges)
                {
                    options.KnownIPNetworks.Add(range);
                }
            });

        services.AddSingleton<UntrustedProxyLog>();

        return services;
    }

    /// <summary>
    /// Takes the scheme and client address a trusted proxy forwards, so everything after sees the client's own,
    /// and warns once per address about a proxy that sent forwarded headers without being trusted.
    /// </summary>
    public static IApplicationBuilder UseTrustedProxies(this WebApplication app)
    {
        // The framework's variable makes its own startup filter run the middleware ahead of this pipeline, and a second
        // pass would consume a second X-Forwarded-For value, taking the client's own as its address.
        if (string.Equals(app.Configuration["ForwardedHeaders_Enabled"], "true", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(app.Configuration[TrustedProxyOptions.Setting]))
            {
                LogTrustsAnySender(app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(TrustedProxies).FullName!));
            }
        }
        else
        {
            app.UseForwardedHeaders();
        }

        app.Use(async (context, next) =>
        {
            var peer = context.Connection.RemoteIpAddress;
            var headers = context.Request.Headers;
            var forwardedHeadersLeftAlone =
                (headers.ContainsKey("X-Forwarded-For") || headers.ContainsKey("X-Forwarded-Proto"))
                && !headers.ContainsKey("X-Original-For") && !headers.ContainsKey("X-Original-Proto");
            if (peer is not null && forwardedHeadersLeftAlone)
            {
                var trust = context.RequestServices.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
                if (!IsTrusted(trust, peer))
                {
                    context.RequestServices.GetRequiredService<UntrustedProxyLog>().Warn(peer);
                }
            }

            await next(context);
        });

        return app;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "ASPNETCORE_FORWARDEDHEADERS_ENABLED is set, so Nook trusts forwarded headers from any sender. Set Nook:TrustedProxies to your proxy's address instead.")]
    private static partial void LogTrustsAnySender(ILogger logger);

    /// <summary>An IPv4-mapped IPv6 address as the IPv4 address it stands for, and any other address as it is.</summary>
    internal static IPAddress InIPv4Form(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    // Reads the lists the middleware reads: with both empty everyone is trusted.
    private static bool IsTrusted(ForwardedHeadersOptions options, IPAddress peer)
    {
        if (options.KnownProxies.Count == 0 && options.KnownIPNetworks.Count == 0)
        {
            return true;
        }

        peer = InIPv4Form(peer);
        return options.KnownProxies.Any(proxy => InIPv4Form(proxy).Equals(peer))
            || options.KnownIPNetworks.Any(range => range.Contains(peer));
    }
}

/// <summary>The peers already warned about, so a proxy Nook doesn't trust is named once, and a burst of callers can't grow the list without bound.</summary>
public sealed partial class UntrustedProxyLog(ILogger<UntrustedProxyLog> logger)
{
    private const int Capacity = 100;

    private readonly ConcurrentDictionary<IPAddress, byte> _warned = new();

    /// <summary>Warns about <paramref name="peer"/> the first time, naming it in its IPv4 form when it arrives IPv4-mapped.</summary>
    public void Warn(IPAddress peer)
    {
        var address = TrustedProxies.InIPv4Form(peer);
        if (_warned.Count < Capacity && _warned.TryAdd(address, 0))
        {
            LogUntrustedProxy(logger, address.ToString());
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A request came through a proxy at {Address} that Nook does not trust, so Nook ignored its X-Forwarded headers. If that is your reverse proxy, add {Address} to Nook:TrustedProxies.")]
    private partial void LogUntrustedProxy(ILogger logger, string address);
}
