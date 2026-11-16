using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;

namespace Nook.Api.Features.Sections;

public record SectionResponse(Guid Id, string Name);

[ApiController]
[Route("api/sections")]
public class SectionsController(NookDbContext db) : ControllerBase
{
    /// <summary>The sections the signed-in user can see, by name.</summary>
    [HttpGet]
    public async Task<List<SectionResponse>> List(CancellationToken cancellationToken) =>
        await db.Sections
            .VisibleTo(User)
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .Select(s => new SectionResponse(s.Id, s.Name))
            .ToListAsync(cancellationToken);
}
