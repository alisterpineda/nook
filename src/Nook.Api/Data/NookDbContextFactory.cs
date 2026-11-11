using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Nook.Api.Data;

/// <summary>
/// Builds the context for <c>dotnet ef</c>, so migrations can be generated without a running database.
/// Keep the options in step with the registration in <c>Program.cs</c>.
/// </summary>
public class NookDbContextFactory : IDesignTimeDbContextFactory<NookDbContext>
{
    public NookDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NookDbContext>()
            .UseNpgsql("Host=localhost;Database=nookdb")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new NookDbContext(options);
    }
}
