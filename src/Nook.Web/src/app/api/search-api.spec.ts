import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SearchApi, SearchResponse } from './search-api';

describe('SearchApi', () => {
  let api: SearchApi;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(SearchApi);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('asks /api/search for the term and the limit, and answers what it returns', async () => {
    const response: SearchResponse = {
      groups: [
        {
          source: 'works',
          total: 9,
          results: [
            {
              id: '0198a1b2-0000-7000-8000-000000000001',
              title: 'Project Hail Mary',
              subtitle: 'Andy Weir',
            },
          ],
        },
      ],
    };

    const searching = api.search({ Q: 'hail', Limit: 6 });
    const request = httpTesting.expectOne((req) => req.url === '/api/search');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('Q')).toBe('hail');
    expect(request.request.params.get('Limit')).toBe('6');
    request.flush(response);

    expect(await searching).toEqual(response);
  });
});
