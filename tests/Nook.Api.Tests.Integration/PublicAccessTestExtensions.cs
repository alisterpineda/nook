using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Nook.Api.Data;
using Nook.Api.Features.PublicAccess;

namespace Nook.Api.Tests.Integration;

/// <summary>Saving the public access settings the way an admin does.</summary>
internal static class PublicAccessTestExtensions
{
    /// <summary>
    /// Saves every public access setting through the API as a new admin. Each save replaces the last, so a section
    /// left out has no level.
    /// </summary>
    public static async Task SetPublicAccessAsync(
        this WebApplicationFactory<Program> factory,
        bool enabled,
        ContentAccess access,
        ContentAccess defaultArea = ContentAccess.None,
        params (Guid SectionId, ContentAccess Access)[] sections)
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var response = await admin.PutPublicAccessAsync(new PublicAccessRequest(
            enabled,
            access,
            defaultArea,
            sections.Select(s => new PublicAccessSectionRequest(s.SectionId, s.Access)).ToList()));
        Assert.True(response.IsSuccessStatusCode, $"PUT /api/public-access answered {(int)response.StatusCode}.");
    }

    public static Task<HttpResponseMessage> PutPublicAccessAsync(this HttpClient client, PublicAccessRequest request) =>
        client.PutAsJsonAsync("/api/public-access", request, ApiTestExtensions.Json, TestContext.Current.CancellationToken);
}
