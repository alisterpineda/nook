import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { components } from './schema';
import { WorksApi, coverUrl, partFileUrl } from './works-api';

type PageOfWorkSummaryResponse = components['schemas']['PageOfWorkSummaryResponse'];
type WorkDetailResponse = components['schemas']['WorkDetailResponse'];
type WorkEditRequest = components['schemas']['WorkEditRequest'];

describe('WorksApi', () => {
  let api: WorksApi;
  let httpTesting: HttpTestingController;

  const emptyPage: PageOfWorkSummaryResponse = { items: [], total: 0, offset: 0, limit: 50 };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(WorksApi);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('list sends the section, sort, offset and limit as query parameters', async () => {
    const page: PageOfWorkSummaryResponse = {
      items: [
        {
          id: '0198a1b2-0000-7000-8000-000000000001',
          sectionId: '0198a1b2-0000-7000-8000-00000000000a',
          title: 'Dune',
          authors: ['Frank Herbert'],
          series: { id: '0198a1b2-0000-7000-8000-0000000000c1', name: 'Dune', position: 1 },
          coverHash: null,
          formats: ['epub'],
          addedAt: '2026-11-01T12:00:00Z',
        },
      ],
      total: 51,
      offset: 50,
      limit: 50,
    };

    const listing = api.list({
      SectionId: '0198a1b2-0000-7000-8000-00000000000a',
      Sort: 'added',
      Offset: 50,
      Limit: 50,
    });
    const request = httpTesting.expectOne((req) => req.url === '/api/works');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('SectionId')).toBe('0198a1b2-0000-7000-8000-00000000000a');
    expect(request.request.params.get('Sort')).toBe('added');
    expect(request.request.params.get('Offset')).toBe('50');
    expect(request.request.params.get('Limit')).toBe('50');
    request.flush(page);

    expect(await listing).toEqual(page);
  });

  it('list leaves out the section when none is given', async () => {
    const listing = api.list({ Sort: 'title', Offset: 0, Limit: 50 });
    const request = httpTesting.expectOne((req) => req.url === '/api/works');
    expect(request.request.params.has('SectionId')).toBe(false);
    expect(request.request.params.keys()).toEqual(['Sort', 'Offset', 'Limit']);
    request.flush(emptyPage);

    expect(await listing).toEqual(emptyPage);
  });

  it('list sends the search term as a query parameter', async () => {
    const listing = api.list({ Search: 'hail mary', Sort: 'relevance', Offset: 0, Limit: 50 });
    const request = httpTesting.expectOne((req) => req.url === '/api/works');
    expect(request.request.params.get('Search')).toBe('hail mary');
    expect(request.request.params.get('Sort')).toBe('relevance');
    request.flush(emptyPage);

    expect(await listing).toEqual(emptyPage);
  });

  it('get requests the work by id', async () => {
    const work: WorkDetailResponse = {
      id: '0198a1b2-0000-7000-8000-000000000001',
      sectionId: '0198a1b2-0000-7000-8000-00000000000a',
      title: 'Dune',
      sortTitle: 'Dune',
      description: null,
      language: 'en',
      publicationDate: '1965-08-01',
      publisher: 'Chilton Books',
      series: null,
      coverHash: null,
      contributors: [],
      tags: [],
      externalIds: [],
      items: [],
      addedAt: '2026-11-01T12:00:00Z',
      updatedAt: '2026-11-01T12:00:00Z',
      access: 'download',
      canEdit: false,
      addedBy: null,
    };

    const fetching = api.get('0198a1b2-0000-7000-8000-000000000001');
    const request = httpTesting.expectOne('/api/works/0198a1b2-0000-7000-8000-000000000001');
    expect(request.request.method).toBe('GET');
    request.flush(work);

    expect(await fetching).toEqual(work);
  });

  it("sends an edit as a PUT to the work's address", async () => {
    const edit: WorkEditRequest = {
      title: 'Dune Messiah',
      sortTitle: 'Dune Messiah',
      authors: ['Frank Herbert'],
      series: { name: 'Dune', position: 2 },
      tags: ['Science fiction'],
      description: null,
      sectionId: '0198a1b2-0000-7000-8000-00000000000a',
    };

    const saving = api.update('a/b', edit);
    const request = httpTesting.expectOne('/api/works/a%2Fb');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(edit);
    request.flush(null, { status: 204, statusText: 'No Content' });

    await expect(saving).resolves.toBeUndefined();
  });

  it("sends a delete to the work's address", async () => {
    const deleting = api.delete('a/b');
    const request = httpTesting.expectOne('/api/works/a%2Fb');
    expect(request.request.method).toBe('DELETE');
    expect(request.request.body).toBeNull();
    request.flush(null, { status: 204, statusText: 'No Content' });

    await expect(deleting).resolves.toBeUndefined();
  });
});

describe('work file URLs', () => {
  it('coverUrl names the work, the size and the cover hash', () => {
    expect(coverUrl('0198a1b2-0000-7000-8000-000000000001', 'abc123', 'small')).toBe(
      '/api/works/0198a1b2-0000-7000-8000-000000000001/cover?size=small&v=abc123',
    );
    expect(coverUrl('0198a1b2-0000-7000-8000-000000000001', 'abc123', 'large')).toBe(
      '/api/works/0198a1b2-0000-7000-8000-000000000001/cover?size=large&v=abc123',
    );
  });

  it('partFileUrl names the part', () => {
    expect(partFileUrl('0198a1b2-0000-7000-8000-0000000000f2')).toBe(
      '/api/parts/0198a1b2-0000-7000-8000-0000000000f2/file',
    );
  });
});
