using System.Net;
using System.Text.RegularExpressions;
using Testcontainers.Keycloak;

namespace Nook.Api.Tests.Integration;

/// <summary>
/// Starts a Keycloak with the realm <c>nook</c> from <c>Keycloak/nook-realm.json</c>: the client Nook signs in as,
/// the groups <c>nook-admins</c> and <c>nook-members</c>, and the users the single sign-on tests sign in with.
/// Use it as a class fixture, so Keycloak starts only for the classes that need it.
/// </summary>
public sealed partial class KeycloakFixture : IAsyncLifetime
{
    /// <summary>The password of every user in the realm.</summary>
    public const string Password = "nook-test-password";

    private readonly KeycloakContainer _container = new KeycloakBuilder("quay.io/keycloak/keycloak:26.8.2")
        .WithRealm(Path.Combine(AppContext.BaseDirectory, "Keycloak", "nook-realm.json"))
        .Build();

    // Keycloak's cookies are Secure and it answers over plain http, so they are copied by hand rather than kept
    // by a cookie container.
    private readonly HttpClient _browser = new(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });

    /// <summary>The issuer of the realm <c>nook</c>, as Nook's <c>Nook:Oidc:Authority</c>.</summary>
    public string Authority => $"{_container.GetBaseAddress()}realms/nook";

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync()
    {
        _browser.Dispose();
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Signs in to Nook as a realm user: starts single sign-on at Nook, signs in at Keycloak with the user's
    /// password, and passes Keycloak's answer back to Nook's callback.
    /// </summary>
    /// <param name="nook">A client of Nook over https that keeps cookies and does not follow redirects.</param>
    /// <returns>Nook's answer to the callback.</returns>
    public async Task<HttpResponseMessage> SignInAsync(HttpClient nook, string username)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var login = await nook.GetAsync("/api/auth/oidc/login", cancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        var page = await _browser.GetAsync(login.Headers.Location, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync(cancellationToken);
        var form = LoginForm().Match(html);
        Assert.True(form.Success, "Keycloak's answer has no login form.");
        var action = WebUtility.HtmlDecode(FormAction().Match(form.Value).Groups["action"].Value);

        using var post = new HttpRequestMessage(HttpMethod.Post, action)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = username,
                ["password"] = Password,
                ["credentialId"] = "",
            }),
        };
        post.Headers.Add("Cookie", string.Join("; ", page.SetCookies().Select(cookie => cookie.Split(';')[0])));

        var answer = await _browser.SendAsync(post, cancellationToken);
        Assert.True(
            answer.StatusCode == HttpStatusCode.Found,
            $"Keycloak answered the sign-in of {username} with {(int)answer.StatusCode}.");

        return await nook.GetAsync(answer.Headers.Location!.PathAndQuery, cancellationToken);
    }

    [GeneratedRegex("""<form\b[^>]*\bid="kc-form-login"[^>]*>""")]
    private static partial Regex LoginForm();

    [GeneratedRegex(@"\baction=""(?<action>[^""]*)""")]
    private static partial Regex FormAction();
}
