using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features.Audit;
using Nook.Api.Features.PublicAccess;

namespace Nook.Api.Tests.Integration;

public class PublicAccessApiTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    private const string Url = "/api/public-access";

    [Fact]
    public async Task Admin_saves_public_access_and_reads_it_back()
    {
        var (fiction, poetry, history) = (TestData.NewSection("Fiction"), TestData.NewSection("Poetry"), TestData.NewSection("History"));
        await factory.AddRowsAsync(fiction, poetry, history);
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        // A section above the Anonymous level is kept as saved; the Anonymous level caps it where it is used.
        var response = await admin.PutPublicAccessAsync(new PublicAccessRequest(
            true,
            ContentAccess.Read,
            ContentAccess.Browse,
            [new(fiction.Id, ContentAccess.Download), new(poetry.Id, ContentAccess.Browse), new(history.Id, ContentAccess.None)]));
        Assert.True(response.IsSuccessStatusCode, $"PUT answered {(int)response.StatusCode}.");

        var saved = await admin.GetAsync<PublicAccessResponse>(Url);
        Assert.True(saved.Enabled);
        Assert.Equal(ContentAccess.Read, saved.Access);
        Assert.Equal(ContentAccess.Browse, saved.DefaultAreaAccess);
        Assert.Equal(ContentAccess.Download, LevelOf(saved, fiction));
        Assert.Equal(ContentAccess.Browse, LevelOf(saved, poetry));
        Assert.Equal(ContentAccess.None, LevelOf(saved, history));
        Assert.Equal("Poetry", saved.Sections.Single(s => s.Id == poetry.Id).Name);
        Assert.Equivalent(new[] { fiction.Id, poetry.Id }, await AnonymousSectionRowsAsync(), strict: true);

        // A save replaces everything: a section left out loses its level.
        await factory.SetPublicAccessAsync(false, ContentAccess.Browse, ContentAccess.None, (history.Id, ContentAccess.Browse));

        var replaced = await admin.GetAsync<PublicAccessResponse>(Url);
        Assert.False(replaced.Enabled);
        Assert.Equal(ContentAccess.Browse, replaced.Access);
        Assert.Equal(ContentAccess.None, replaced.DefaultAreaAccess);
        Assert.Equal(ContentAccess.None, LevelOf(replaced, fiction));
        Assert.Equal(ContentAccess.None, LevelOf(replaced, poetry));
        Assert.Equal(ContentAccess.Browse, LevelOf(replaced, history));
        Assert.Equivalent(new[] { history.Id }, await AnonymousSectionRowsAsync(), strict: true);
    }

    [Fact]
    public async Task Saved_public_access_outlasts_a_restart()
    {
        var section = TestData.NewSection("Essays");
        await factory.AddRowsAsync(section);
        await factory.SetPublicAccessAsync(true, ContentAccess.Download, ContentAccess.Read, (section.Id, ContentAccess.Read));

        // A second host over the same data folder and database stands for the API after a restart.
        await using var restarted = factory.WithWebHostBuilder(_ => { });
        using var admin = await restarted.CreateSignedInClientAsync(BuiltInRole.Admin);
        var settings = await admin.GetAsync<PublicAccessResponse>(Url);

        Assert.True(settings.Enabled);
        Assert.Equal(ContentAccess.Download, settings.Access);
        Assert.Equal(ContentAccess.Read, settings.DefaultAreaAccess);
        Assert.Equal(ContentAccess.Read, LevelOf(settings, section));
    }

    [Fact]
    public async Task A_level_for_an_unknown_section_is_refused()
    {
        var section = TestData.NewSection("Drama");
        await factory.AddRowsAsync(section);
        await factory.SetPublicAccessAsync(true, ContentAccess.Browse, ContentAccess.None, (section.Id, ContentAccess.Browse));
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var before = await admin.GetAsync<PublicAccessResponse>(Url);

        var response = await admin.PutPublicAccessAsync(new PublicAccessRequest(
            false,
            ContentAccess.Download,
            ContentAccess.Download,
            [new(section.Id, ContentAccess.Read), new(Guid.NewGuid(), ContentAccess.Browse)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await admin.GetAsync<PublicAccessResponse>(Url);
        Assert.Equal(
            (before.Enabled, before.Access, before.DefaultAreaAccess, LevelOf(before, section)),
            (after.Enabled, after.Access, after.DefaultAreaAccess, LevelOf(after, section)));
    }

    [Fact]
    public async Task A_section_given_two_levels_is_refused()
    {
        var section = TestData.NewSection("Letters");
        await factory.AddRowsAsync(section);
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutPublicAccessAsync(new PublicAccessRequest(
            true,
            ContentAccess.Download,
            ContentAccess.None,
            [new(section.Id, ContentAccess.Read), new(section.Id, ContentAccess.Browse)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"enabled":true,"access":99,"defaultAreaAccess":"none","sections":[]}""")]
    [InlineData("""{"enabled":true,"access":"read","defaultAreaAccess":-1,"sections":[]}""")]
    [InlineData("""{"enabled":true,"access":"read","defaultAreaAccess":"none","sections":[{"id":"SECTION","access":7}]}""")]
    public async Task A_level_that_is_not_one_of_the_four_is_refused(string body)
    {
        var section = TestData.NewSection("Maps");
        await factory.AddRowsAsync(section);
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        using var content = new StringContent(
            body.Replace("SECTION", section.Id.ToString()), System.Text.Encoding.UTF8, "application/json");

        var response = await admin.PutAsync(Url, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Saves_made_at_the_same_moment_all_succeed()
    {
        var section = TestData.NewSection("Atlases");
        await factory.AddRowsAsync(section);
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var request = new PublicAccessRequest(
            true, ContentAccess.Read, ContentAccess.None, [new(section.Id, ContentAccess.Read)]);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => admin.PutPublicAccessAsync(request)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NoContent, response.StatusCode));
    }

    [Theory]
    [InlineData(BuiltInRole.Contributor)]
    [InlineData(BuiltInRole.Viewer)]
    public async Task Contributor_or_viewer_is_forbidden_from_public_access_settings(BuiltInRole role)
    {
        using var client = await factory.CreateSignedInClientAsync(role);

        var read = await client.GetAsync(Url, TestContext.Current.CancellationToken);
        var save = await client.PutPublicAccessAsync(Everything());

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, save.StatusCode);
    }

    [Fact]
    public async Task Public_access_settings_without_a_session_are_unauthorized()
    {
        using var client = factory.CreateClient();

        var read = await client.GetAsync(Url, TestContext.Current.CancellationToken);
        var save = await client.PutAsJsonAsync(Url, Everything(), ApiTestExtensions.Json, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, save.StatusCode);
    }

    [Fact]
    public async Task Saving_public_access_is_logged_with_the_settings_before_and_after()
    {
        var (fiction, poetry) = (TestData.NewSection("Fiction"), TestData.NewSection("Poetry"));
        await factory.AddRowsAsync(fiction, poetry);
        await factory.SetPublicAccessAsync(false, ContentAccess.Browse, ContentAccess.None, (fiction.Id, ContentAccess.Browse));
        var (admin, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Admin, "Pat Public");
        using var clientSession = client;
        var before = await client.GetAsync<PublicAccessResponse>(Url);

        var response = await client.PutPublicAccessAsync(new PublicAccessRequest(
            true, ContentAccess.Read, ContentAccess.Browse, [new(poetry.Id, ContentAccess.Download)]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = await client.GetAsync<PublicAccessResponse>(Url);
        var entry = Assert.Single(await ChangeEntriesAsync(admin.Id));
        Assert.Equal(admin.Id, entry.ActorId);
        Assert.Equal("Pat Public", entry.ActorName);
        Assert.Equal(admin.Email, entry.ActorEmail);
        Assert.Equal(("public-access", (Guid?)null, "Public access"), (entry.SubjectType, entry.SubjectId, entry.SubjectName));
        using var details = System.Text.Json.JsonDocument.Parse(entry.Details);
        Assert.Equal(before, details.RootElement.GetProperty("before").Deserialize<PublicAccessResponse>(ApiTestExtensions.Json), PublicAccessComparer.Instance);
        Assert.Equal(after, details.RootElement.GetProperty("after").Deserialize<PublicAccessResponse>(ApiTestExtensions.Json), PublicAccessComparer.Instance);
        Assert.False(details.RootElement.GetProperty("before").GetProperty("enabled").GetBoolean());
        Assert.True(details.RootElement.GetProperty("after").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task A_refused_save_is_not_logged()
    {
        var section = TestData.NewSection("Plays");
        await factory.AddRowsAsync(section);
        var (user, admin) = await factory.CreateSignedInUserAsync(BuiltInRole.Admin);
        using var adminSession = admin;

        var response = await admin.PutPublicAccessAsync(new PublicAccessRequest(
            true, ContentAccess.Read, ContentAccess.None, [new(section.Id, ContentAccess.Read), new(Guid.NewGuid(), ContentAccess.Browse)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await ChangeEntriesAsync(user.Id));
    }

    [Fact]
    public async Task A_save_that_runs_again_after_a_lost_commit_answer_is_logged_once()
    {
        var section = TestData.NewSection("Zines");
        await factory.AddRowsAsync(section);
        var marker = section.Id.ToString();
        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<NookDbContext>(options =>
                options.AddInterceptors(new LostCommitAnswerInterceptor(context =>
                    context.ChangeTracker.Entries<AuditEntry>().Select(e => e.Entity).FirstOrDefault(e =>
                        e.Action == AuditActions.PublicAccessChanged
                        && e.Details.Contains(marker, StringComparison.Ordinal))?.Id)))));
        var (user, admin) = await app.CreateSignedInUserAsync(BuiltInRole.Admin);
        using var adminSession = admin;

        var response = await admin.PutPublicAccessAsync(new PublicAccessRequest(
            true, ContentAccess.Read, ContentAccess.None, [new(section.Id, ContentAccess.Read)]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(await ChangeEntriesAsync(user.Id));
        var saved = await admin.GetAsync<PublicAccessResponse>(Url);
        Assert.Equal(ContentAccess.Read, LevelOf(saved, section));
    }

    private async Task<List<AuditEntry>> ChangeEntriesAsync(Guid actorId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        return await db.AuditEntries
            .Where(e => e.Action == AuditActions.PublicAccessChanged && e.ActorId == actorId)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private sealed class PublicAccessComparer : IEqualityComparer<PublicAccessResponse>
    {
        public static readonly PublicAccessComparer Instance = new();

        public bool Equals(PublicAccessResponse? x, PublicAccessResponse? y) =>
            x is not null && y is not null
            && (x.Enabled, x.Access, x.DefaultAreaAccess) == (y.Enabled, y.Access, y.DefaultAreaAccess)
            && x.Sections.Select(s => (s.Id, s.Name, s.Access)).SequenceEqual(y.Sections.Select(s => (s.Id, s.Name, s.Access)));

        public int GetHashCode(PublicAccessResponse obj) => 0;
    }

    private static PublicAccessRequest Everything() =>
        new(true, ContentAccess.Download, ContentAccess.Download, []);

    private static ContentAccess LevelOf(PublicAccessResponse settings, LibrarySection section) =>
        settings.Sections.Single(s => s.Id == section.Id).Access;

    /// <summary>The sections the Anonymous role has a row for.</summary>
    private async Task<List<Guid>> AnonymousSectionRowsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        return await db.RoleSections
            .Where(r => r.RoleId == Role.AnonymousId)
            .Select(r => r.SectionId)
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
