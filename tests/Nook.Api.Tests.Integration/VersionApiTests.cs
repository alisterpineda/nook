using System.Net;
using Nook.Api.Data;
using Nook.Api.Features.Build;

namespace Nook.Api.Tests.Integration;

public class VersionApiTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Theory]
    [InlineData(BuiltInRole.Admin)]
    [InlineData(BuiltInRole.Contributor)]
    [InlineData(BuiltInRole.Viewer)]
    public async Task Any_signed_in_role_reads_the_version(BuiltInRole role)
    {
        using var client = await factory.CreateSignedInClientAsync(role);

        var response = await client.GetAsync("/api/version", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BuildVersion.Current, (await response.ReadAsync<VersionResponse>()).Version);
    }

    [Fact]
    public async Task Signed_out_caller_is_not_told_the_version()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/version", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
