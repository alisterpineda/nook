using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features;
using Nook.Api.Features.Audit;
using Nook.Api.Features.Works;

namespace Nook.Api.Tests.Integration;

public class WorkEditTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Edit_saves_a_new_title_sort_title_and_description()
    {
        var section = TestData.NewSection("Edited works");
        var work = TestData.NewWork("Before", section.Id);
        await factory.AddRowsAsync(section, work);
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(work.Id, Request(section.Id, title: "  After  ", sortTitle: "After, The", description: "A new blurb."));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await admin.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}");
        Assert.Equal("After", detail.Title);
        Assert.Equal("After, The", detail.SortTitle);
        Assert.Equal("A new blurb.", detail.Description);
    }

    [Fact]
    public async Task Edit_by_the_owner_is_saved()
    {
        var section = TestData.NewSection("Owned and edited");
        var (owner, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor);
        using var session = client;
        var work = TestData.NewWork("Mine", section.Id);
        work.OwnerId = owner.Id;
        await factory.AddRowsAsync(section, work);

        var response = await client.PutWorkAsync(work.Id, Request(section.Id, title: "Still mine"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("Still mine", (await client.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}")).Title);
    }

    [Fact]
    public async Task Edit_by_a_contributor_who_does_not_own_the_work_is_refused()
    {
        var section = TestData.NewSection("Someone else's work");
        var work = TestData.NewWork("Not yours", section.Id);
        await factory.AddRowsAsync(section, work);
        using var contributor = await factory.CreateSignedInClientAsync(BuiltInRole.Contributor);

        var response = await contributor.PutWorkAsync(work.Id, Request(section.Id, title: "Taken over"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Not yours", (await contributor.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}")).Title);
    }

    [Fact]
    public async Task Edit_by_a_viewer_is_refused()
    {
        var section = TestData.NewSection("A viewer's own work");
        var (viewer, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Viewer);
        using var session = client;
        var work = TestData.NewWork("Owned by a viewer", section.Id);
        work.OwnerId = viewer.Id;
        await factory.AddRowsAsync(section, work);

        var response = await client.PutWorkAsync(work.Id, Request(section.Id, title: "Changed"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Owned by a viewer", (await client.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}")).Title);
    }

    [Fact]
    public async Task Edit_with_no_session_is_refused()
    {
        var section = TestData.NewSection("Open to visitors");
        var work = TestData.NewWork("Seen by anyone", section.Id);
        await factory.AddRowsAsync(section, work);
        await factory.SetPublicAccessAsync(true, ContentAccess.Read, ContentAccess.None, (section.Id, ContentAccess.Download));
        using var visitor = factory.CreateClient();

        var response = await visitor.PutWorkAsync(work.Id, Request(section.Id, title: "Defaced"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Seen by anyone", (await visitor.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}")).Title);
    }

    [Fact]
    public async Task Edit_of_a_work_the_caller_cannot_see_answers_not_found()
    {
        var hidden = TestData.NewSection("Hidden work");
        var visible = TestData.NewSection("Visible work");
        var role = ApiTestExtensions.NewRole("Sees one section", canUpload: true, allSections: false, defaultArea: ContentAccess.None);
        await factory.AddRowsAsync(hidden, visible, role);
        await factory.AddRowsAsync(new RoleSection { RoleId = role.Id, SectionId = visible.Id, Access = ContentAccess.Browse });
        var (user, client) = await factory.CreateSignedInUserAsync(role.Id);
        using var signedIn = client;
        var work = TestData.NewWork("Out of sight", hidden.Id);
        work.OwnerId = user.Id;
        await factory.AddRowsAsync(work);

        var response = await client.PutWorkAsync(work.Id, Request(hidden.Id, title: "Changed"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Out of sight", (await factory.ReadWorkAsync(work.Id)).Title);
    }

    [Fact]
    public async Task Edit_with_an_empty_title_is_refused()
    {
        var section = TestData.NewSection("Empty title");
        var work = TestData.NewWork("Has a title", section.Id);
        await factory.AddRowsAsync(section, work);
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(work.Id, Request(section.Id, title: "   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Has a title", (await admin.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}")).Title);
    }

    [Fact]
    public async Task Edit_with_a_blank_author_name_is_refused()
    {
        var section = TestData.NewSection("Blank author");
        var work = TestData.NewWork("Authorless", section.Id);
        await factory.AddRowsAsync(section, work);
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(work.Id, Request(section.Id, title: "Changed", authors: ["Real Name", "  "]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Authorless", (await admin.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}")).Title);
    }

    [Fact]
    public async Task Edit_naming_one_author_twice_in_different_case_is_refused()
    {
        var section = TestData.NewSection("Repeated author");
        var work = TestData.NewWork("Repeats", section.Id);
        await factory.AddRowsAsync(section, work);
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(work.Id, Request(section.Id, title: "Changed", authors: ["Ann Leckie", "ann  LECKIE"]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Repeats", (await admin.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}")).Title);
    }

    [Fact]
    public async Task Edit_naming_one_tag_twice_is_refused()
    {
        var section = TestData.NewSection("Repeated tag");
        var work = TestData.NewWork("Tagged twice", section.Id);
        await factory.AddRowsAsync(section, work);
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(work.Id, Request(section.Id, title: "Changed", tags: ["Space", "space"]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Tagged twice", (await admin.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}")).Title);
    }

    [Fact]
    public async Task Edit_with_a_new_description_over_the_limit_is_refused()
    {
        var section = TestData.NewSection("Long description");
        var work = TestData.NewWork("Short blurb", section.Id);
        work.Description = "Short.";
        await factory.AddRowsAsync(section, work);
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(work.Id, Request(section.Id, title: "Short blurb", description: new string('x', 20_001)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Short.", (await admin.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}")).Description);
    }

    [Fact]
    public async Task Edit_of_the_title_is_saved_for_a_work_whose_stored_description_is_over_the_limit()
    {
        var section = TestData.NewSection("Stored long description");
        var work = TestData.NewWork("Long blurb", section.Id);
        work.Description = new string('y', 25_000);
        await factory.AddRowsAsync(section, work);
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(work.Id, Request(section.Id, title: "Long blurb, retitled", description: work.Description));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await admin.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}");
        Assert.Equal("Long blurb, retitled", detail.Title);
        Assert.Equal(25_000, detail.Description!.Length);
    }

    [Fact]
    public async Task Edit_shows_the_authors_in_the_order_sent()
    {
        var section = TestData.NewSection("Author order");
        var work = TestData.NewWork("Three authors", section.Id);
        var suffix = IngestTestExtensions.UniqueSuffix();
        var (a, b, c) = (NewPerson($"Adams {suffix}"), NewPerson($"Brown {suffix}"), NewPerson($"Clarke {suffix}"));
        await factory.AddRowsAsync(section, work, a, b, c, Author(work, a, 0), Author(work, b, 1), Author(work, c, 2));
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(
            work.Id, Request(section.Id, title: "Three authors", authors: [c.Name, a.Name, b.Name]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await admin.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}");
        Assert.Equal([c.Name, a.Name, b.Name], detail.Contributors.Select(x => x.Name));
    }

    [Fact]
    public async Task Edit_links_a_name_that_differs_only_in_case_to_the_row_already_there()
    {
        var section = TestData.NewSection("Case match");
        var work = TestData.NewWork("Relinked", section.Id);
        var suffix = IngestTestExtensions.UniqueSuffix();
        var person = NewPerson($"Octavia Butler {suffix}");
        var series = new Series { Name = $"Patternist {suffix}" };
        var tag = new Tag { Name = $"Dystopia {suffix}" };
        await factory.AddRowsAsync(section, work, person, series, tag);
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(work.Id, Request(
            section.Id,
            title: "Relinked",
            authors: [$"OCTAVIA   butler {suffix}".ToUpperInvariant()],
            series: new WorkEditSeriesRequest($"patternist {suffix}", 2),
            tags: [$"DYSTOPIA {suffix}"]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await admin.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}");
        Assert.Equal(person.Id, Assert.Single(detail.Contributors).PersonId);
        Assert.Equal(series.Id, detail.Series!.Id);
        Assert.Equal(2, detail.Series.Position);
        Assert.Equal(tag.Id, Assert.Single(detail.Tags).Id);
        Assert.Equal(1, await factory.CountNamedAsync<Person>(suffix));
        Assert.Equal(1, await factory.CountNamedAsync<Series>(suffix));
        Assert.Equal(1, await factory.CountNamedAsync<Tag>(suffix));
    }

    [Fact]
    public async Task Edit_makes_a_row_for_a_name_not_seen_before()
    {
        var section = TestData.NewSection("New names");
        var work = TestData.NewWork("Fresh names", section.Id);
        await factory.AddRowsAsync(section, work);
        var suffix = IngestTestExtensions.UniqueSuffix();
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(work.Id, Request(
            section.Id,
            title: "Fresh names",
            authors: [$"  Nnedi   Okorafor {suffix} "],
            series: new WorkEditSeriesRequest($"Binti {suffix}", 1.5m),
            tags: [$"Afrofuturism {suffix}"]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await admin.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}");
        Assert.Equal($"Nnedi Okorafor {suffix}", Assert.Single(detail.Contributors).Name);
        Assert.Equal($"Binti {suffix}", detail.Series!.Name);
        Assert.Equal(1.5m, detail.Series.Position);
        Assert.Equal($"Afrofuturism {suffix}", Assert.Single(detail.Tags).Name);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var person = await db.People.SingleAsync(p => p.Name.EndsWith(suffix), TestContext.Current.CancellationToken);
        Assert.Equal($"Nnedi Okorafor {suffix}", person.SortName);
    }

    [Fact]
    public async Task Edit_leaves_another_work_with_the_author_series_and_tags_it_had()
    {
        var section = TestData.NewSection("Two works");
        var edited = TestData.NewWork("Edited", section.Id);
        var other = TestData.NewWork("Untouched", section.Id);
        var suffix = IngestTestExtensions.UniqueSuffix();
        var person = NewPerson($"Shared Author {suffix}");
        var series = new Series { Name = $"Shared Series {suffix}" };
        var tag = new Tag { Name = $"Shared Tag {suffix}" };
        edited.SeriesId = series.Id;
        other.SeriesId = series.Id;
        other.SeriesPosition = 3;
        edited.Tags.Add(tag);
        other.Tags.Add(tag);
        await factory.AddRowsAsync(section, person, series, tag, edited, other, Author(edited, person, 0), Author(other, person, 0));
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(edited.Id, Request(
            section.Id,
            title: "Edited",
            authors: [$"Someone Else {suffix}"],
            series: new WorkEditSeriesRequest($"Another Series {suffix}", 1),
            tags: [$"Another Tag {suffix}"]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await admin.GetAsync<WorkDetailResponse>($"/api/works/{other.Id}");
        Assert.Equal(person.Id, Assert.Single(detail.Contributors).PersonId);
        Assert.Equal(series.Id, detail.Series!.Id);
        Assert.Equal(3, detail.Series.Position);
        Assert.Equal(tag.Id, Assert.Single(detail.Tags).Id);
    }

    [Fact]
    public async Task Edit_removes_a_person_series_and_tag_it_leaves_with_no_work()
    {
        var section = TestData.NewSection("Orphans");
        var work = TestData.NewWork("Orphaning", section.Id);
        var suffix = IngestTestExtensions.UniqueSuffix();
        var person = NewPerson($"Only Here {suffix}");
        var series = new Series { Name = $"Only Series {suffix}" };
        var tag = new Tag { Name = $"Only Tag {suffix}" };
        work.SeriesId = series.Id;
        work.Tags.Add(tag);
        await factory.AddRowsAsync(section, person, series, tag, work, Author(work, person, 0));
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(work.Id, Request(section.Id, title: "Orphaning"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await factory.CountNamedAsync<Person>(suffix));
        Assert.Equal(0, await factory.CountNamedAsync<Series>(suffix));
        Assert.Equal(0, await factory.CountNamedAsync<Tag>(suffix));
        var detail = await admin.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}");
        Assert.Empty(detail.Contributors);
        Assert.Null(detail.Series);
        Assert.Empty(detail.Tags);
    }

    [Fact]
    public async Task Edit_keeps_a_person_another_work_still_names()
    {
        var section = TestData.NewSection("Kept person");
        var edited = TestData.NewWork("Dropping the author", section.Id);
        var other = TestData.NewWork("Keeping the author", section.Id);
        var suffix = IngestTestExtensions.UniqueSuffix();
        var person = NewPerson($"Still Named {suffix}");
        await factory.AddRowsAsync(section, person, edited, other, Author(edited, person, 0), Author(other, person, 0));
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(edited.Id, Request(section.Id, title: "Dropping the author"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, await factory.CountNamedAsync<Person>(suffix));
    }

    [Fact]
    public async Task Edit_keeps_a_translator_after_the_authors()
    {
        var section = TestData.NewSection("Translated");
        var work = TestData.NewWork("In translation", section.Id);
        var suffix = IngestTestExtensions.UniqueSuffix();
        var author = NewPerson($"Original Author {suffix}");
        var translator = NewPerson($"Helpful Translator {suffix}");
        await factory.AddRowsAsync(
            section, work, author, translator,
            Author(work, author, 0),
            new WorkContributor { WorkId = work.Id, PersonId = translator.Id, Role = ContributorRole.Translator, Order = 1 });
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(work.Id, Request(
            section.Id, title: "In translation", authors: [$"First New {suffix}", $"Second New {suffix}"]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await admin.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}");
        Assert.Equal(
            [$"First New {suffix}", $"Second New {suffix}", translator.Name],
            detail.Contributors.Select(x => x.Name));
        Assert.Equal(ContributorRole.Translator, detail.Contributors[2].Role);
    }

    [Fact]
    public async Task Edit_by_the_owner_moves_the_work_to_a_section_their_role_can_see()
    {
        var from = TestData.NewSection("Moving from");
        var to = TestData.NewSection("Moving to");
        var (owner, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor);
        using var session = client;
        var work = TestData.NewWork("On the move", from.Id);
        work.OwnerId = owner.Id;
        await factory.AddRowsAsync(from, to, work);

        var response = await client.PutWorkAsync(work.Id, Request(to.Id, title: "On the move"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(to.Id, (await client.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}")).SectionId);
    }

    [Fact]
    public async Task Edit_into_a_section_the_owners_role_cannot_see_is_refused()
    {
        var from = TestData.NewSection("Seen section");
        var to = TestData.NewSection("Unseen section");
        var role = ApiTestExtensions.NewRole("Sees one section", canUpload: true, allSections: false, defaultArea: ContentAccess.None);
        await factory.AddRowsAsync(from, to, role);
        await factory.AddRowsAsync(new RoleSection { RoleId = role.Id, SectionId = from.Id, Access = ContentAccess.Browse });
        var (owner, client) = await factory.CreateSignedInUserAsync(role.Id);
        using var session = client;
        var work = TestData.NewWork("Staying put", from.Id);
        work.OwnerId = owner.Id;
        await factory.AddRowsAsync(work);

        var response = await client.PutWorkAsync(work.Id, Request(to.Id, title: "Staying put"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(from.Id, (await factory.ReadWorkAsync(work.Id)).SectionId);
    }

    [Fact]
    public async Task Edit_into_a_section_that_does_not_exist_is_refused()
    {
        var section = TestData.NewSection("Real section");
        var work = TestData.NewWork("Going nowhere", section.Id);
        await factory.AddRowsAsync(section, work);
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(work.Id, Request(Guid.CreateVersion7(), title: "Going nowhere"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(section.Id, (await factory.ReadWorkAsync(work.Id)).SectionId);
    }

    [Fact]
    public async Task Edit_moves_the_work_to_the_default_area_when_no_section_is_sent()
    {
        var section = TestData.NewSection("Leaving for Default");
        var work = TestData.NewWork("Unsectioned soon", section.Id);
        await factory.AddRowsAsync(section, work);
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(work.Id, Request(sectionId: null, title: "Unsectioned soon"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null((await admin.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}")).SectionId);
    }

    [Fact]
    public async Task Edited_work_is_found_by_its_new_title_author_and_tag_and_not_by_the_old_ones()
    {
        var section = TestData.NewSection("Searched after an edit");
        var work = TestData.NewWork("Zorblax", section.Id);
        var person = NewPerson("Quillfeather");
        var tag = new Tag { Name = "Wombat" };
        work.Tags.Add(tag);
        await factory.AddSearchableRowsAsync(section, person, tag, work, Author(work, person, 0));
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(
            work.Id, Request(section.Id, title: "Glimmerton", authors: ["Thistledown"], tags: ["Marmoset"]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        foreach (var found in new[] { "glimmerton", "thistledown", "marmoset" })
        {
            Assert.Equal([work.Id], await SearchAsync(admin, found, section.Id));
        }

        foreach (var gone in new[] { "zorblax", "quillfeather", "wombat" })
        {
            Assert.Empty(await SearchAsync(admin, gone, section.Id));
        }
    }

    [Fact]
    public async Task Edit_records_the_old_and_new_value_of_each_field_it_changed_and_no_other()
    {
        var section = TestData.NewSection("Audited edit");
        var (user, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Admin);
        using var session = client;
        var work = TestData.NewWork("Old title", section.Id);
        var tag = new Tag { Name = $"Old tag {IngestTestExtensions.UniqueSuffix()}" };
        work.Tags.Add(tag);
        await factory.AddRowsAsync(section, tag, work);

        var response = await client.PutWorkAsync(work.Id, Request(section.Id, title: "New title", sortTitle: work.SortTitle, tags: ["New tag"]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var entry = Assert.Single(await EntriesAsync(work.Id));
        Assert.Equal(user.Id, entry.ActorId);
        Assert.Equal(AuditSubjectTypes.Work, entry.SubjectType);
        Assert.Equal("New title", entry.SubjectName);
        using var details = JsonDocument.Parse(entry.Details);
        var changes = details.RootElement.GetProperty("changes");
        Assert.Equal(["tags", "title"], changes.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Old title", changes.GetProperty("title").GetProperty("from").GetString());
        Assert.Equal("New title", changes.GetProperty("title").GetProperty("to").GetString());
        Assert.Equal([tag.Name], changes.GetProperty("tags").GetProperty("from").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["New tag"], changes.GetProperty("tags").GetProperty("to").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Edit_records_a_changed_author_series_and_section_with_their_old_and_new_values()
    {
        var from = TestData.NewSection("Audited from");
        var to = TestData.NewSection("Audited to");
        var work = TestData.NewWork("Moved and renamed", from.Id);
        var suffix = IngestTestExtensions.UniqueSuffix();
        var person = NewPerson($"Before Author {suffix}");
        await factory.AddRowsAsync(from, to, person, work, Author(work, person, 0));
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(work.Id, Request(
            to.Id,
            title: work.Title,
            authors: [$"After Author {suffix}"],
            series: new WorkEditSeriesRequest($"Fresh Series {suffix}", 2)));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var details = JsonDocument.Parse(Assert.Single(await EntriesAsync(work.Id)).Details);
        var changes = details.RootElement.GetProperty("changes");
        Assert.Equal(["authors", "section", "series"], changes.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal([person.Name], changes.GetProperty("authors").GetProperty("from").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal([$"After Author {suffix}"], changes.GetProperty("authors").GetProperty("to").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(JsonValueKind.Null, changes.GetProperty("series").GetProperty("from").ValueKind);
        Assert.Equal($"Fresh Series {suffix}", changes.GetProperty("series").GetProperty("to").GetProperty("name").GetString());
        Assert.Equal(2, changes.GetProperty("series").GetProperty("to").GetProperty("position").GetDecimal());
        Assert.Equal(from.Id, changes.GetProperty("section").GetProperty("from").GetProperty("id").GetGuid());
        Assert.Equal("Audited to", changes.GetProperty("section").GetProperty("to").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Edit_that_changes_nothing_records_nothing()
    {
        var section = TestData.NewSection("Unchanged edit");
        var work = TestData.NewWork("Same as ever", section.Id);
        var suffix = IngestTestExtensions.UniqueSuffix();
        var person = NewPerson($"Steady Author {suffix}");
        var tag = new Tag { Name = $"Steady Tag {suffix}" };
        work.Tags.Add(tag);
        await factory.AddRowsAsync(section, person, tag, work, Author(work, person, 0));
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        // The same names in another letter case are the same rows, so nothing changed.
        var response = await admin.PutWorkAsync(work.Id, Request(
            section.Id, title: work.Title, sortTitle: work.SortTitle, authors: [person.Name.ToUpperInvariant()], tags: [tag.Name.ToUpperInvariant()]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await EntriesAsync(work.Id));
    }

    private async Task<List<AuditEntry>> EntriesAsync(Guid workId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        return await db.AuditEntries
            .Where(e => e.Action == AuditActions.WorkEdited && e.SubjectId == workId)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<List<Guid>> SearchAsync(HttpClient client, string words, Guid sectionId)
    {
        var page = await client.GetAsync<Page<WorkSummaryResponse>>($"/api/works?search={words}&sectionId={sectionId}");
        return [.. page.Items.Select(w => w.Id)];
    }

    private static Person NewPerson(string name) => new() { Name = name, SortName = name };

    private static WorkContributor Author(Work work, Person person, int order) =>
        new() { WorkId = work.Id, PersonId = person.Id, Role = ContributorRole.Author, Order = order };

    /// <summary>An edit that sends the values given and nothing else, in the section given.</summary>
    private static WorkEditRequest Request(
        Guid? sectionId,
        string title = "A title",
        string? sortTitle = null,
        List<string>? authors = null,
        WorkEditSeriesRequest? series = null,
        List<string>? tags = null,
        string? description = null) =>
        new(title, sortTitle ?? title, authors ?? [], series, tags ?? [], description, sectionId);
}

internal static class WorkEditTestExtensions
{
    public static Task<HttpResponseMessage> PutWorkAsync(this HttpClient client, Guid id, WorkEditRequest request) =>
        client.PutAsJsonAsync($"/api/works/{id}", request, TestContext.Current.CancellationToken);
}
