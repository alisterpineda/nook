import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { components } from './schema';
import { SectionsApi } from './sections-api';

type SectionResponse = components['schemas']['SectionResponse'];

describe('SectionsApi', () => {
  let api: SectionsApi;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(SectionsApi);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('list returns the sections the API sends', async () => {
    const sections: SectionResponse[] = [
      { id: '0198a1b2-0000-7000-8000-00000000000a', name: 'Books' },
      { id: '0198a1b2-0000-7000-8000-00000000000b', name: 'Comics' },
    ];

    const listing = api.list();
    const request = httpTesting.expectOne('/api/sections');
    expect(request.request.method).toBe('GET');
    request.flush(sections);

    expect(await listing).toEqual(sections);
  });
});
