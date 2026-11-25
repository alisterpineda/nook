using Nook.Api.Features.Build;

namespace Nook.Api.Tests.Unit;

public class BuildVersionTests
{
    [Fact]
    public void The_build_reports_the_placeholder_version_and_a_short_commit()
    {
        Assert.Matches(@"^0\.0\.0-dev\+[0-9a-f]{7}$", BuildVersion.Current);
    }
}
