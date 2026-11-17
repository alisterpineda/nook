using Nook.Api.Data;

namespace Nook.Api.Features.PublicAccess;

/// <summary>A section and the Anonymous role's level in it, <see cref="ContentAccess.None"/> when it has none.</summary>
public record PublicAccessSectionResponse(Guid Id, string Name, ContentAccess Access);

/// <summary>
/// Every public access setting: whether it is on, the Anonymous role's level, its level in the Default area, and its
/// level in every section, by name.
/// </summary>
public record PublicAccessResponse(
    bool Enabled,
    ContentAccess Access,
    ContentAccess DefaultAreaAccess,
    List<PublicAccessSectionResponse> Sections);

/// <summary>The Anonymous role's level in one section.</summary>
public record PublicAccessSectionRequest(Guid Id, ContentAccess Access);

/// <summary>
/// Every public access setting, replacing the saved ones. A section left out, or given
/// <see cref="ContentAccess.None"/>, has no level.
/// </summary>
public record PublicAccessRequest(
    bool Enabled,
    ContentAccess Access,
    ContentAccess DefaultAreaAccess,
    List<PublicAccessSectionRequest> Sections);

/// <summary>Whether public access is on, and the caller's level in the Default area.</summary>
public record AccessResponse(bool PublicAccess, ContentAccess DefaultArea);
