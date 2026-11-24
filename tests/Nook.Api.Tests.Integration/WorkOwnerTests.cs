using Nook.Api.Data;
using Nook.Api.Features.Works;
using Nook.Api.Jobs;

namespace Nook.Api.Tests.Integration;

public class WorkOwnerTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Upload_that_creates_a_work_makes_the_uploader_its_owner()
    {
        var section = await factory.AddSectionAsync();
        var (user, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor);
        using var clientSession = client;
        var epub = new EpubBuilder().WithTitle("The Dispossessed").WithCreator($"Ursula Le Guin {IngestTestExtensions.UniqueSuffix()}").Build();

        var workId = await UploadAndWaitAsync(client, epub, "dispossessed.epub", section.Id);

        Assert.Equal(user.Id, (await factory.ReadWorkAsync(workId)).OwnerId);
    }

    [Fact]
    public async Task Upload_that_joins_a_work_leaves_its_owner_as_it_was()
    {
        var section = await factory.AddSectionAsync();
        var (first, firstClient) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor);
        using var firstSession = firstClient;
        var (_, secondClient) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor);
        using var secondSession = secondClient;
        var suffix = IngestTestExtensions.UniqueSuffix();
        var author = $"Ursula K. Le Guin {suffix}";
        var firstEpub = new EpubBuilder().WithTitle($"Lavinia {suffix}").WithCreator(author)
            .WithIdentifier("9780547572482", scheme: "isbn")
            .Build();
        var secondEpub = new EpubBuilder().WithTitle($"Lavinia (Reissue) {suffix}").WithCreator(author)
            .WithIdentifier("9780547572482", scheme: "isbn")
            .Build();

        var workId = await UploadAndWaitAsync(firstClient, firstEpub, "lavinia.epub", section.Id);
        var joinedId = await UploadAndWaitAsync(secondClient, secondEpub, "lavinia-reissue.epub", section.Id);

        Assert.Equal(workId, joinedId);
        Assert.Equal(first.Id, (await factory.ReadWorkAsync(workId)).OwnerId);
    }

    [Fact]
    public async Task Work_detail_says_an_admin_and_the_owner_may_edit_and_no_one_else()
    {
        var section = TestData.NewSection("Owned works");
        var (contributor, contributorClient) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor);
        using var contributorSession = contributorClient;
        var (viewer, viewerClient) = await factory.CreateSignedInUserAsync(BuiltInRole.Viewer);
        using var viewerSession = viewerClient;
        var contributed = TestData.NewWork("Owned by a contributor", section.Id);
        contributed.OwnerId = contributor.Id;
        var viewed = TestData.NewWork("Owned by a viewer", section.Id);
        viewed.OwnerId = viewer.Id;
        await factory.AddRowsAsync(section, contributed, viewed);
        await factory.SetPublicAccessAsync(true, ContentAccess.Read, ContentAccess.None, (section.Id, ContentAccess.Browse));
        using var otherContributor = await factory.CreateSignedInClientAsync(BuiltInRole.Contributor);
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        using var visitor = factory.CreateClient();

        var asOwner = await contributorClient.GetAsync<WorkDetailResponse>($"/api/works/{contributed.Id}");
        var asOther = await otherContributor.GetAsync<WorkDetailResponse>($"/api/works/{contributed.Id}");
        var asAdmin = await admin.GetAsync<WorkDetailResponse>($"/api/works/{contributed.Id}");
        var asVisitor = await visitor.GetAsync<WorkDetailResponse>($"/api/works/{contributed.Id}");
        var asViewer = await viewerClient.GetAsync<WorkDetailResponse>($"/api/works/{viewed.Id}");

        Assert.Equal([true, false, true, false], [asOwner.CanEdit, asOther.CanEdit, asAdmin.CanEdit, asVisitor.CanEdit]);
        Assert.False(asViewer.CanEdit);
    }

    private async Task<Guid> UploadAndWaitAsync(HttpClient client, byte[] bytes, string fileName, Guid sectionId)
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var job = await client.IngestAsync(bytes, fileName, sectionId, waitThrough: admin);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        return job.ReadIngestResult().WorkId;
    }
}
