using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Nook.Api.Data;

namespace Nook.Api.Features.Auth;

public static class AuthServiceExtensions
{
    /// <summary>
    /// Adds sign-in with a cookie, an authorization default that requires a signed-in user on every endpoint,
    /// and the rate limit for the endpoints that take a password.
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

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();

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
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        var permitLimit = configuration.GetValue("RateLimiting:Auth:PermitLimit", 10);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(RateLimits.Auth, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(1) }));
        });

        return services;
    }
}

/// <summary>The names of Nook's rate limits.</summary>
public static class RateLimits
{
    /// <summary>Endpoints that take a password, limited per client address.</summary>
    public const string Auth = "auth";
}
