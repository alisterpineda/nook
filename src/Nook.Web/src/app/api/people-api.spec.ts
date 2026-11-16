import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PeopleApi } from './people-api';
import { components } from './schema';

type PageOfPersonSummaryResponse = components['schemas']['PageOfPersonSummaryResponse'];
type PersonDetailResponse = components['schemas']['PersonDetailResponse'];

describe('PeopleApi', () => {
  let api: PeopleApi;
  let httpTesting: HttpTestingController;

  const emptyPage: PageOfPersonSummaryResponse = { items: [], total: 0, offset: 0, limit: 50 };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(PeopleApi);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('list sends the role, offset and limit as query parameters', async () => {
    const page: PageOfPersonSummaryResponse = {
      items: [
        {
          id: '0198a1b2-0000-7000-8000-0000000000b1',
          name: 'Frank Herbert',
          sortName: 'Herbert, Frank',
          workCount: 3,
        },
      ],
      total: 51,
      offset: 50,
      limit: 50,
    };

    const listing = api.list({ Role: 'author', Offset: 50, Limit: 50 });
    const request = httpTesting.expectOne((req) => req.url === '/api/people');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('Role')).toBe('author');
    expect(request.request.params.get('Offset')).toBe('50');
    expect(request.request.params.get('Limit')).toBe('50');
    request.flush(page);

    expect(await listing).toEqual(page);
  });

  it('list leaves out the role when none is given', async () => {
    const listing = api.list({ Offset: 0, Limit: 50 });
    const request = httpTesting.expectOne((req) => req.url === '/api/people');
    expect(request.request.params.has('Role')).toBe(false);
    expect(request.request.params.keys()).toEqual(['Offset', 'Limit']);
    request.flush(emptyPage);

    expect(await listing).toEqual(emptyPage);
  });

  it('get requests the person by id', async () => {
    const person: PersonDetailResponse = {
      id: '0198a1b2-0000-7000-8000-0000000000b1',
      name: 'Frank Herbert',
      sortName: 'Herbert, Frank',
      roles: [
        {
          role: 'author',
          works: [
            {
              id: '0198a1b2-0000-7000-8000-000000000001',
              sectionId: null,
              title: 'Dune',
              authors: ['Frank Herbert'],
              series: null,
              coverHash: null,
              formats: ['epub'],
              addedAt: '2026-11-01T12:00:00Z',
            },
          ],
        },
      ],
    };

    const fetching = api.get('0198a1b2-0000-7000-8000-0000000000b1');
    const request = httpTesting.expectOne('/api/people/0198a1b2-0000-7000-8000-0000000000b1');
    expect(request.request.method).toBe('GET');
    request.flush(person);

    expect(await fetching).toEqual(person);
  });
});
