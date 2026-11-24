using System.Net;
using Nook.Api.Data;
using Nook.Api.Features;
using Nook.Api.Features.People;
using Nook.Api.Features.PublicAccess;
using Nook.Api.Features.Sections;
using Nook.Api.Features.Works;

namespace Nook.Api.Tests.Integration;

/// <summary>
/// What a visitor with no session finds in the catalog with public access on. Each test saves the settings it needs
/// first, and a save replaces every level, so sections other tests added are closed.
/// </summary>
public class PublicAccessCatalogTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Visitor_lists_only_the_works_and_sections_open_to_browsing()
    {
        var (open, closed) = (TestData.NewSection("Open"), TestData.NewSection("Closed"));
        var (first, second) = (TestData.NewWork("First", open.Id), TestData.NewWork("Second", open.Id));
        var hidden = TestData.NewWork("Hidden", closed.Id);
        var inDefault = TestData.NewWork("Unsectioned");
        await factory.AddRowsAsync(open, closed, first, second, hidden, inDefault);
        await factory.SetPublicAccessAsync(
            true, ContentAccess.Download, ContentAccess.None, (open.Id, ContentAccess.Browse));
        using var visitor = factory.CreateClient();

        var works = await visitor.GetAsync<Page<WorkSummaryResponse>>("/api/works?limit=200");
        var ofClosed = await visitor.GetAsync<Page<WorkSummaryResponse>>($"/api/works?sectionId={closed.Id}");
        var sections = await visitor.GetAsync<List<SectionResponse>>("/api/sections");

        Assert.Equivalent(new[] { first.Id, second.Id }, works.Items.Select(w => w.Id), strict: true);
        Assert.Equal(2, works.Total);
        Assert.Empty(ofClosed.Items);
        Assert.Equal(0, ofClosed.Total);
        Assert.Equal([new SectionResponse(open.Id, "Open")], sections);
    }

    [Fact]
    public async Task Visitor_gets_a_work_open_to_browsing_and_not_found_for_any_other()
    {
        var (open, closed) = (TestData.NewSection("Open"), TestData.NewSection("Closed"));
        var (shown, hidden) = (TestData.NewWork("Shown", open.Id), TestData.NewWork("Hidden", closed.Id));
        var inDefault = TestData.NewWork("Unsectioned");
        await factory.AddRowsAsync(open, closed, shown, hidden, inDefault);
        await factory.SetPublicAccessAsync(true, ContentAccess.Read, ContentAccess.None, (open.Id, ContentAccess.Browse));
        using var visitor = factory.CreateClient();

        var detail = await visitor.GetAsync<WorkDetailResponse>($"/api/works/{shown.Id}");

        Assert.Equal((shown.Id, "Shown"), (detail.Id, detail.Title));
        Assert.Equal(HttpStatusCode.NotFound, await StatusOfAsync(visitor, $"/api/works/{hidden.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusOfAsync(visitor, $"/api/works/{inDefault.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusOfAsync(visitor, $"/api/works/{Guid.CreateVersion7()}"));
    }

    [Fact]
    public async Task Work_detail_leaves_out_who_added_it_for_a_visitor()
    {
        var open = TestData.NewSection("Open");
        var owner = await factory.CreateUserAsync(displayName: "Ada Adder");
        var work = TestData.NewWork("Added by someone", open.Id);
        work.OwnerId = owner.Id;
        await factory.AddRowsAsync(open, work);
        await factory.SetPublicAccessAsync(true, ContentAccess.Read, ContentAccess.None, (open.Id, ContentAccess.Browse));
        using var visitor = factory.CreateClient();

        var raw = await visitor.GetStringAsync($"/api/works/{work.Id}", TestContext.Current.CancellationToken);

        Assert.Contains("Added by someone", raw);
        Assert.DoesNotContain("addedBy", raw);
        Assert.DoesNotContain("Ada Adder", raw);
    }

    [Fact]
    public async Task Visitor_sees_the_default_area_only_at_its_own_level()
    {
        var title = $"Unsectioned {Guid.NewGuid():N}";
        var work = TestData.NewWork(title);
        await factory.AddRowsAsync(work);
        using var visitor = factory.CreateClient();

        await factory.SetPublicAccessAsync(true, ContentAccess.Download, ContentAccess.None);
        var closed = await visitor.GetAsync<Page<WorkSummaryResponse>>("/api/works?noSection=true&limit=200");
        var closedStatus = await StatusOfAsync(visitor, $"/api/works/{work.Id}");

        await factory.SetPublicAccessAsync(true, ContentAccess.Download, ContentAccess.Browse);
        var opened = await visitor.GetAsync<Page<WorkSummaryResponse>>("/api/works?noSection=true&limit=200");
        var openedStatus = await StatusOfAsync(visitor, $"/api/works/{work.Id}");

        // The Anonymous level caps the Default area's own.
        await factory.SetPublicAccessAsync(true, ContentAccess.None, ContentAccess.Download);
        var capped = await visitor.GetAsync<Page<WorkSummaryResponse>>("/api/works?noSection=true&limit=200");
        var cappedStatus = await StatusOfAsync(visitor, $"/api/works/{work.Id}");

        Assert.Empty(closed.Items);
        Assert.Equal(0, closed.Total);
        Assert.Equal(HttpStatusCode.NotFound, closedStatus);
        Assert.Contains(opened.Items, w => w.Id == work.Id);
        Assert.Equal(HttpStatusCode.OK, openedStatus);
        Assert.Empty(capped.Items);
        Assert.Equal(0, capped.Total);
        Assert.Equal(HttpStatusCode.NotFound, cappedStatus);
    }

    [Fact]
    public async Task A_section_level_above_the_anonymous_level_is_capped()
    {
        var section = TestData.NewSection("Capped");
        var work = TestData.NewWork("Capped work", section.Id);
        await factory.AddRowsAsync(section, work);
        await factory.SetPublicAccessAsync(true, ContentAccess.None, ContentAccess.None, (section.Id, ContentAccess.Download));
        using var visitor = factory.CreateClient();

        var works = await visitor.GetAsync<Page<WorkSummaryResponse>>("/api/works?limit=200");
        var sections = await visitor.GetAsync<List<SectionResponse>>("/api/sections");

        Assert.DoesNotContain(works.Items, w => w.Id == work.Id);
        Assert.DoesNotContain(sections, s => s.Id == section.Id);
        Assert.Equal(HttpStatusCode.NotFound, await StatusOfAsync(visitor, $"/api/works/{work.Id}"));
    }

    [Fact]
    public async Task Signed_in_user_keeps_every_section_with_public_access_on()
    {
        var (open, closed) = (TestData.NewSection("Open"), TestData.NewSection("Closed"));
        var (shown, hidden) = (TestData.NewWork("Shown", open.Id), TestData.NewWork("Hidden", closed.Id));
        await factory.AddRowsAsync(open, closed, shown, hidden);
        await factory.SetPublicAccessAsync(true, ContentAccess.Browse, ContentAccess.None, (open.Id, ContentAccess.Browse));
        using var viewer = await factory.CreateSignedInClientAsync(BuiltInRole.Viewer);

        var sections = await viewer.GetAsync<List<SectionResponse>>("/api/sections");
        var detail = await viewer.GetAsync<WorkDetailResponse>($"/api/works/{hidden.Id}");

        Assert.Contains(sections, s => s.Id == closed.Id);
        Assert.Equal(hidden.Id, detail.Id);
    }

    [Fact]
    public async Task A_work_states_the_callers_level()
    {
        var (browsed, read) = (TestData.NewSection("Browsed"), TestData.NewSection("Read"));
        var (inBrowsed, inRead) = (TestData.NewWork("Browsed work", browsed.Id), TestData.NewWork("Read work", read.Id));
        await factory.AddRowsAsync(browsed, read, inBrowsed, inRead);
        await factory.SetPublicAccessAsync(
            true, ContentAccess.Download, ContentAccess.None,
            (browsed.Id, ContentAccess.Browse), (read.Id, ContentAccess.Read));
        using var visitor = factory.CreateClient();
        using var viewer = await factory.CreateSignedInClientAsync(BuiltInRole.Viewer);

        var visitorBrowses = await visitor.GetAsync<WorkDetailResponse>($"/api/works/{inBrowsed.Id}");
        var visitorReads = await visitor.GetAsync<WorkDetailResponse>($"/api/works/{inRead.Id}");
        var viewerDownloads = await viewer.GetAsync<WorkDetailResponse>($"/api/works/{inBrowsed.Id}");

        Assert.Equal(ContentAccess.Browse, visitorBrowses.Access);
        Assert.Equal(ContentAccess.Read, visitorReads.Access);
        Assert.Equal(ContentAccess.Download, viewerDownloads.Access);
    }

    [Fact]
    public async Task Visitor_is_unauthorized_on_uploads_jobs_and_public_access_settings()
    {
        await factory.SetPublicAccessAsync(true, ContentAccess.Download, ContentAccess.Download);
        using var visitor = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var jobId = Guid.CreateVersion7();

        var responses = new[]
        {
            await visitor.PostAsync("/api/uploads", new MultipartFormDataContent(), cancellationToken),
            await visitor.GetAsync("/api/jobs", cancellationToken),
            await visitor.GetAsync($"/api/jobs/{jobId}", cancellationToken),
            await visitor.PostAsync($"/api/jobs/{jobId}/retry", null, cancellationToken),
            await visitor.GetAsync("/api/public-access", cancellationToken),
            await visitor.PutPublicAccessAsync(new(false, ContentAccess.None, ContentAccess.None, [])),
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
    }

    [Fact]
    public async Task Visitor_fetches_a_cover_only_for_a_work_open_to_browsing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (open, closed) = (TestData.NewSection("Open"), TestData.NewSection("Closed"));
        var folder = factory.LibraryPath("Visitor covers");
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "cover.png"), TestData.PngCover, cancellationToken);
        var (shown, hidden) = (TestData.NewWork("Shown", open.Id), TestData.NewWork("Hidden", closed.Id));
        foreach (var work in new[] { shown, hidden })
        {
            work.CoverHash = "0123456789abcdef";
            work.CoverPath = "Visitor covers/cover.png";
        }
        await factory.AddRowsAsync(open, closed, shown, hidden);
        await factory.SetPublicAccessAsync(true, ContentAccess.Browse, ContentAccess.None, (open.Id, ContentAccess.Browse));
        using var visitor = factory.CreateClient();

        var response = await visitor.GetAsync($"/api/works/{shown.Id}/cover", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TestData.PngCover, await response.Content.ReadAsByteArrayAsync(cancellationToken));
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.Equal(HttpStatusCode.NotFound, await StatusOfAsync(visitor, $"/api/works/{hidden.Id}/cover"));
    }

    [Fact]
    public async Task Visitor_lists_people_by_the_works_open_to_browsing()
    {
        var (open, closed) = (TestData.NewSection("Open"), TestData.NewSection("Closed"));
        var shared = new Person { Name = "Sam Shared", SortName = "Shared, Sam" };
        var hiddenOnly = new Person { Name = "Hal Hidden", SortName = "Hidden, Hal" };
        var shown = TestData.NewWork("Shown", open.Id);
        shown.Contributors.Add(new WorkContributor { PersonId = shared.Id });
        var hidden = TestData.NewWork("Hidden", closed.Id);
        hidden.Contributors.Add(new WorkContributor { PersonId = shared.Id, Order = 0 });
        hidden.Contributors.Add(new WorkContributor { PersonId = hiddenOnly.Id, Order = 1 });
        await factory.AddRowsAsync(open, closed, shared, hiddenOnly, shown, hidden);
        await factory.SetPublicAccessAsync(true, ContentAccess.Browse, ContentAccess.None, (open.Id, ContentAccess.Browse));
        using var visitor = factory.CreateClient();

        var people = await visitor.GetAsync<Page<PersonSummaryResponse>>("/api/people?limit=200");
        var person = await visitor.GetAsync<PersonDetailResponse>($"/api/people/{shared.Id}");

        Assert.Equal(
            [new PersonSummaryResponse(shared.Id, "Sam Shared", "Shared, Sam", 1)],
            people.Items.Where(p => p.Id == shared.Id || p.Id == hiddenOnly.Id));
        Assert.Equal([shown.Id], person.Roles.SelectMany(r => r.Works).Select(w => w.Id));
    }

    [Fact]
    public async Task A_person_credited_only_on_closed_works_is_not_found_for_a_visitor()
    {
        var closed = TestData.NewSection("Closed");
        var person = new Person { Name = "Hal Hidden", SortName = "Hidden, Hal" };
        var work = TestData.NewWork("Hidden", closed.Id);
        work.Contributors.Add(new WorkContributor { PersonId = person.Id });
        var inDefault = TestData.NewWork("Unsectioned");
        inDefault.Contributors.Add(new WorkContributor { PersonId = person.Id });
        await factory.AddRowsAsync(closed, person, work, inDefault);
        await factory.SetPublicAccessAsync(true, ContentAccess.Download, ContentAccess.None);
        using var visitor = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, await StatusOfAsync(visitor, $"/api/people/{person.Id}"));
    }

    private static async Task<HttpStatusCode> StatusOfAsync(HttpClient client, string url) =>
        (await client.GetAsync(url, TestContext.Current.CancellationToken)).StatusCode;
}
