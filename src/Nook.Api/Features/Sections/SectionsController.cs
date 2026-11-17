using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Features.Auth;

namespace Nook.Api.Features.Sections;

public record SectionResponse(Guid Id, string Name);

[ApiController]
[Route("api/sections")]
[Authorize(Policy = Policies.Catalog)]
public class SectionsController(NookDbContext db, SectionAccess access) : ControllerBase
{
    /// <summary>The sections the caller can see, by name.</summary>
    [HttpGet]
    public async Task<List<SectionResponse>> List(CancellationToken cancellationToken) =>
        await db.Sections
            .VisibleTo(await access.ForAsync(User, cancellationToken))
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .Select(s => new SectionResponse(s.Id, s.Name))
            .ToListAsync(cancellationToken);
}
