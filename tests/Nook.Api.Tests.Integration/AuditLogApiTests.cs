using System.Net;
using System.Text.Json;
using Nook.Api.Data;
using Nook.Api.Features;
using Nook.Api.Features.Audit;

namespace Nook.Api.Tests.Integration;

public class AuditLogApiTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task The_log_comes_newest_first_in_pages()
    {
        var actor = Guid.NewGuid();
        var first = await AddEntryAsync(actor, "Ann", "ann@example.test", "upload.added");
        var second = await AddEntryAsync(actor, "Ann", "ann@example.test", "upload.added");
        var third = await AddEntryAsync(actor, "Ann", "ann@example.test", "upload.added", """{"note":"kept as written"}""");
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var top = await client.GetAsync<Page<AuditEntryResponse>>($"/api/audit-log?actorId={actor}&limit=2");
        var next = await client.GetAsync<Page<AuditEntryResponse>>($"/api/audit-log?actorId={actor}&limit=1&offset=2");

        Assert.Equal([third, second], top.Items.Select(e => e.Id));
        Assert.Equal((3, 0, 2), (top.Total, top.Offset, top.Limit));
        Assert.Equal([first], next.Items.Select(e => e.Id));
        Assert.Equal((3, 2, 1), (next.Total, next.Offset, next.Limit));
        Assert.Equal("kept as written", top.Items[0].Details.GetProperty("note").GetString());
        Assert.Equal("Ann", top.Items[0].ActorName);
        Assert.Equal("ann@example.test", top.Items[0].ActorEmail);

        var defaults = await client.GetAsync<Page<AuditEntryResponse>>("/api/audit-log");
        Assert.Equal(50, defaults.Limit);
    }

    [Fact]
    public async Task The_log_can_be_narrowed_to_one_person_and_one_action()
    {
        var ann = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var action = $"test.{Guid.NewGuid():N}";
        var annOther = await AddEntryAsync(ann, "Ann", "ann@example.test", "upload.failed");
        var annMatch = await AddEntryAsync(ann, "Ann", "ann@example.test", action);
        var bobMatch = await AddEntryAsync(bob, "Bob", "bob@example.test", action);
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var byPerson = await client.GetAsync<Page<AuditEntryResponse>>($"/api/audit-log?actorId={ann}");
        var byAction = await client.GetAsync<Page<AuditEntryResponse>>($"/api/audit-log?action={action}");
        var byBoth = await client.GetAsync<Page<AuditEntryResponse>>($"/api/audit-log?actorId={ann}&action={action}");

        Assert.Equal([annMatch, annOther], byPerson.Items.Select(e => e.Id));
        Assert.Equal(2, byPerson.Total);
        Assert.Equal([bobMatch, annMatch], byAction.Items.Select(e => e.Id));
        Assert.Equal(2, byAction.Total);
        Assert.Equal([annMatch], byBoth.Items.Select(e => e.Id));
        Assert.Equal(1, byBoth.Total);
    }

    [Fact]
    public async Task The_filters_list_everyone_and_every_action_in_the_log()
    {
        var changed = Guid.NewGuid();
        var steady = Guid.NewGuid();
        var action = $"test.{Guid.NewGuid():N}";
        await AddEntryAsync(changed, "Old Name", "old@example.test", "upload.added");
        await AddEntryAsync(changed, "New Name", "new@example.test", action);
        await AddEntryAsync(steady, "Steady", "steady@example.test", "upload.added");
        await AddEntryAsync(null, "Nobody Listed", "nobody@example.test", "upload.added");
        using var client = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var filters = await client.GetAsync<AuditFiltersResponse>("/api/audit-log/filters");

        Assert.Equal(new AuditActorResponse(changed, "New Name", "new@example.test"), Assert.Single(filters.Actors, a => a.Id == changed));
        Assert.Equal(new AuditActorResponse(steady, "Steady", "steady@example.test"), Assert.Single(filters.Actors, a => a.Id == steady));
        Assert.DoesNotContain(filters.Actors, a => a.Name == "Nobody Listed");
        Assert.Contains("upload.added", filters.Actions);
        Assert.Contains(action, filters.Actions);
        Assert.Equal(filters.Actions.Distinct().Order(StringComparer.Ordinal), filters.Actions);
    }

    [Theory]
    [InlineData(BuiltInRole.Contributor, false)]
    [InlineData(BuiltInRole.Contributor, true)]
    [InlineData(BuiltInRole.Viewer, false)]
    [InlineData(BuiltInRole.Viewer, true)]
    public async Task Contributor_or_viewer_is_forbidden_from_reading_the_log(BuiltInRole role, bool filters)
    {
        using var client = await factory.CreateSignedInClientAsync(role);

        var response = await client.GetAsync(filters ? "/api/audit-log/filters" : "/api/audit-log", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> AddEntryAsync(Guid? actorId, string name, string email, string action, string details = "{}")
    {
        var entry = new AuditEntry
        {
            ActorId = actorId,
            ActorName = name,
            ActorEmail = email,
            Action = action,
            SubjectType = "work",
            SubjectId = Guid.NewGuid(),
            SubjectName = "A subject",
            Details = details,
        };
        await factory.AddRowsAsync(entry);
        await Task.Delay(5, TestContext.Current.CancellationToken);
        return entry.Id;
    }
}
