using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Nook.Api.Data;
using Nook.Api.Features.Publications;

namespace Nook.Api.Features.Auth;

public static class AuthServiceExtensions
{
    /// <summary>
    /// Adds sign-in with a cookie, an authorization default that requires a signed-in user on every endpoint, the
    /// catalog policy that also lets a visitor in while public access is on,
    /// the rate limits for the endpoints that take a password and for a visitor's reading and downloading, the single
    /// sign-on settings, and, when single sign-on
    /// is on, the OpenID Connect handler that signs in at the provider.
    /// </summary>
    public static IServiceCollection AddNookAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIdentityCore<NookUser>(options =>
            {
                // Length is what makes a password strong, so no character classes are required.
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                // The user name is the email, so it takes any character an email can hold.
                options.User.AllowedUserNameCharacters = "";

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddEntityFrameworkStores<NookDbContext>()
            .AddSignInManager()
            .AddClaimsPrincipalFactory<NookClaimsPrincipalFactory>();

        services.AddOptions<OidcOptions>()
            .BindConfiguration(OidcOptions.Section)
            .Validate(
                options => options.IsValid(),
                $"{OidcOptions.Section}:ClientId and {OidcOptions.Section}:ClientSecret are required when {OidcOptions.Section}:Authority is set.")
            .ValidateOnStart();

        var authentication = services.AddAuthentication(IdentityConstants.ApplicationScheme);
        authentication.AddIdentityCookies();

        var oidc = configuration.GetSection(OidcOptions.Section).Get<OidcOptions>() ?? new OidcOptions();
        if (oidc.Enabled)
        {
            authentication.AddOpenIdConnect(OidcSignIn.Scheme, options =>
            {
                options.Authority = oidc.Authority;
                options.ClientId = oidc.ClientId;
                options.ClientSecret = oidc.ClientSecret;

                // The authorization code flow, which is the one the handler protects with PKCE.
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.ResponseMode = OpenIdConnectResponseMode.Query;

                // The request goes to the provider in the redirect's query, not pushed to it first, so a provider that
                // offers pushed authorization requests does not change how sign-in starts.
                options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;

                // Under /api, because the web app's dev server passes only /api on to the API.
                options.CallbackPath = "/api/auth/oidc/callback";

                options.Scope.Clear();
                foreach (var scope in oidc.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    options.Scope.Add(scope);
                }

                // Claims keep the names the provider gives them, and the groups and whether the email is verified
                // reach the principal whether the provider sends them in the id token or from userinfo.
                options.GetClaimsFromUserInfoEndpoint = true;
                options.MapInboundClaims = false;
                options.ClaimActions.MapJsonKey(oidc.GroupsClaim, oidc.GroupsClaim);
                options.ClaimActions.MapJsonKey("email_verified", "email_verified");

                options.SaveTokens = false;

                // Nook finds or creates the user and signs them in itself, so the handler never signs in to a
                // cookie of its own.
                options.Events.OnTicketReceived = async context =>
                {
                    string? failure;
                    try
                    {
                        var signIn = context.HttpContext.RequestServices.GetRequiredService<OidcSignIn>();
                        failure = await signIn.SignInAsync(context.Principal!);
                    }
                    catch (Exception exception)
                    {
                        SignInLogger(context.HttpContext)
                            .LogWarning(exception, "Single sign-on failed while signing in to Nook: {Reason}", exception.Message);
                        failure = OidcSignIn.Failed;
                    }

                    context.HandleResponse();
                    context.Response.Redirect(failure is null ? "/" : $"/sign-in?error={failure}");
                };

                // A callback the handler rejects, such as one with an error from the provider or a state it did not
                // issue, goes back to the sign-in page instead of failing the request.
                options.Events.OnRemoteFailure = context =>
                {
                    SignInLogger(context.HttpContext).LogWarning(
                        context.Failure,
                        "Single sign-on failed at the callback: {Reason}",
                        context.Failure?.Message);

                    context.Response.Redirect($"/sign-in?error={OidcSignIn.Failed}");
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        }

        services.AddScoped<OidcSignIn>();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "nook";
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;

            // The API has no login page to send a browser to, so it answers with the status instead of a redirect.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        // The cookie carries the user's role. Rebuilding it this often bounds how long a changed role
        // or a removed user stays signed in with the old one.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(5));

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.Catalog, policy => policy.AddRequirements(new CatalogRequirement()));

        // Scoped, since it reads the settings table.
        services.AddScoped<IAuthorizationHandler, CatalogHandler>();

        var permitLimit = configuration.GetValue("RateLimiting:Auth:PermitLimit", 10);
        var anonymousPermitLimit = configuration.GetValue("RateLimiting:Anonymous:PermitLimit", 300);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(RateLimits.Auth, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientAddress(context),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(1) }));
            // A partition's limiter is made for its key once and kept, so signed-in callers have a key no client
            // address equals. Sharing the address would give a visitor whatever the first caller from it was given.
            options.AddPolicy(RateLimits.AnonymousContent, context => IsSignedInReader(context)
                ? RateLimitPartition.GetNoLimiter("signed-in")
                : RateLimitPartition.GetFixedWindowLimiter(
                    ClientAddress(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = anonymousPermitLimit,
                        Window = TimeSpan.FromMinutes(1),
                    }));
        });

        return services;
    }

    private static string ClientAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    /// Whether the request comes from a signed-in user: one with a session, or one reading under a token path with a
    /// token issued to a user, which a reader's frames reach without the cookie. Whether that user may read the work is
    /// for the endpoint to decide.
    /// </summary>
    private static bool IsSignedInReader(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true || PublicationsController.HasUsersToken(context);

    private static ILogger SignInLogger(HttpContext context) =>
        context.RequestServices.GetRequiredService<ILogger<OidcSignIn>>();
}

/// <summary>The names of Nook's rate limits.</summary>
public static class RateLimits
{
    /// <summary>Endpoints that take a password, limited per client address.</summary>
    public const string Auth = "auth";

    /// <summary>
    /// A visitor's reading and downloading, limited per client address. A signed-in user, and a token issued to one,
    /// is not limited.
    /// </summary>
    public const string AnonymousContent = "anonymous-content";
}
