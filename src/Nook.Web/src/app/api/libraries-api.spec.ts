import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { LibrariesApi } from './libraries-api';
import { components } from './schema';

type LibraryResponse = components['schemas']['LibraryResponse'];

describe('LibrariesApi', () => {
  let api: LibrariesApi;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(LibrariesApi);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('list returns the libraries the API sends', async () => {
    const libraries: LibraryResponse[] = [
      { id: '0198a1b2-0000-7000-8000-00000000000a', name: 'Books' },
      { id: '0198a1b2-0000-7000-8000-00000000000b', name: 'Comics' },
    ];

    const listing = api.list();
    const request = httpTesting.expectOne('/api/libraries');
    expect(request.request.method).toBe('GET');
    request.flush(libraries);

    expect(await listing).toEqual(libraries);
  });
});
