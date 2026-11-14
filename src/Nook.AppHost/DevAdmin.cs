using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Nook.AppHost;

/// <summary>
/// The admin the local stack creates through first-run setup, with the same credentials on every machine, so a dev,
/// a browser test or an agent can sign in without filling in the setup form.
/// </summary>
public static class DevAdmin
{
    public const string Email = "admin@nook.test";

    public const string Password = "nook-dev-password";

    /// <summary>
    /// Sets Nook up with the dev admin through <c>POST api/setup</c> on <paramref name="api"/>. A failed call is
    /// logged as a warning rather than thrown, so a seeding hiccup never stops the stack.
    /// </summary>
    public static async Task EnsureAsync(HttpClient api, ILogger logger, CancellationToken cancellationToken)
    {
        using var response = await PostSetupAsync(api, logger, cancellationToken);
        if (response is null)
        {
            return;
        }

        if (response.IsSuccessStatusCode)
        {
            logger.LogInformation("Created the dev admin. Sign in as {Email} with password {Password}.", Email, Password);
            return;
        }

        // A conflict cannot tell the dev admin from an admin someone else created, so it is not looked into further.
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            logger.LogInformation("Nook was already set up, so no dev admin was created.");
            return;
        }

        logger.LogWarning("The API answered {StatusCode} to setup, so no dev admin was created.", (int)response.StatusCode);
    }

    /// <summary>Posts the dev admin to setup, or logs a warning and returns null when the API cannot be reached.</summary>
    private static async Task<HttpResponseMessage?> PostSetupAsync(HttpClient api, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            return await api.PostAsJsonAsync("api/setup", new { email = Email, password = Password }, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Could not reach the API to create the dev admin: {Reason}", exception.Message);
            return null;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Only the client's own timeout cancels the call while the caller's token is still live.
            logger.LogWarning(exception, "The API did not answer in time to create the dev admin: {Reason}", exception.Message);
            return null;
        }
    }
}
