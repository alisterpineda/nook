using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Nook.Api.Data;
using Nook.Api.Features.Jobs;

namespace Nook.Api.Features.Activity;

/// <summary>What the activity hub sends to a connected client.</summary>
public interface IActivityClient
{
    /// <summary>One of the user's own uploads changed where it stands.</summary>
    Task UploadChanged(UploadActivityResponse upload);

    /// <summary>The job of one of the user's uploads was deleted, and no row of the upload is left.</summary>
    Task UploadRemoved(Guid jobId);

    /// <summary>A job changed status. Sent to admins.</summary>
    Task JobChanged(JobSummaryResponse job);

    /// <summary>A job was deleted. Sent to admins.</summary>
    Task JobRemoved(Guid jobId);
}

/// <summary>
/// Pushes upload and job changes to signed-in users. Each user hears of their own uploads, and admins, who are
/// in the <see cref="AdminsGroup"/> group, hear of every job.
/// </summary>
[Authorize]
public sealed class ActivityHub : Hub<IActivityClient>
{
    /// <summary>The path the hub is served at.</summary>
    public const string Path = "/api/hubs/activity";

    /// <summary>The group admins' connections join.</summary>
    public const string AdminsGroup = "admins";

    public override async Task OnConnectedAsync()
    {
        if (Context.User?.IsInRole(nameof(BuiltInRole.Admin)) == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup);
        }

        await base.OnConnectedAsync();
    }
}

/// <summary>Names a connection's user by the claim Nook's sign-in carries the user's id in.</summary>
public sealed class ActivityUserIdProvider(IOptions<IdentityOptions> options) : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User.FindFirstValue(options.Value.ClaimsIdentity.UserIdClaimType);
}
