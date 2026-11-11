using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;

namespace Nook.Api.Features.Libraries;

public record LibraryResponse(Guid Id, string Name);

[ApiController]
[Route("api/libraries")]
public class LibrariesController(NookDbContext db) : ControllerBase
{
    /// <summary>The libraries the signed-in user can see, by name.</summary>
    [HttpGet]
    public async Task<List<LibraryResponse>> List(CancellationToken cancellationToken) =>
        await db.Libraries
            .VisibleTo(User)
            .OrderBy(l => l.Name)
            .ThenBy(l => l.Id)
            .Select(l => new LibraryResponse(l.Id, l.Name))
            .ToListAsync(cancellationToken);
}
