import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { components } from './schema';
import { WorksApi } from './works-api';

type PageOfWorkSummaryResponse = components['schemas']['PageOfWorkSummaryResponse'];
type WorkDetailResponse = components['schemas']['WorkDetailResponse'];

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

  it('list sends the library, sort, offset and limit as query parameters', async () => {
    const page: PageOfWorkSummaryResponse = {
      items: [
        {
          id: '0198a1b2-0000-7000-8000-000000000001',
          libraryId: '0198a1b2-0000-7000-8000-00000000000a',
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
      LibraryId: '0198a1b2-0000-7000-8000-00000000000a',
      Sort: 'added',
      Offset: 50,
      Limit: 50,
    });
    const request = httpTesting.expectOne((req) => req.url === '/api/works');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('LibraryId')).toBe('0198a1b2-0000-7000-8000-00000000000a');
    expect(request.request.params.get('Sort')).toBe('added');
    expect(request.request.params.get('Offset')).toBe('50');
    expect(request.request.params.get('Limit')).toBe('50');
    request.flush(page);

    expect(await listing).toEqual(page);
  });

  it('list leaves out the library when none is given', async () => {
    const listing = api.list({ Sort: 'title', Offset: 0, Limit: 50 });
    const request = httpTesting.expectOne((req) => req.url === '/api/works');
    expect(request.request.params.has('LibraryId')).toBe(false);
    expect(request.request.params.keys()).toEqual(['Sort', 'Offset', 'Limit']);
    request.flush(emptyPage);

    expect(await listing).toEqual(emptyPage);
  });

  it('get requests the work by id', async () => {
    const work: WorkDetailResponse = {
      id: '0198a1b2-0000-7000-8000-000000000001',
      libraryId: '0198a1b2-0000-7000-8000-00000000000a',
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
    };

    const fetching = api.get('0198a1b2-0000-7000-8000-000000000001');
    const request = httpTesting.expectOne('/api/works/0198a1b2-0000-7000-8000-000000000001');
    expect(request.request.method).toBe('GET');
    request.flush(work);

    expect(await fetching).toEqual(work);
  });
});
