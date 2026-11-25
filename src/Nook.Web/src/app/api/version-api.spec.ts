import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { VersionApi } from './version-api';

describe('VersionApi', () => {
  let api: VersionApi;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(VersionApi);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('asks the API for the version', async () => {
    const version = api.get();
    const request = httpTesting.expectOne('/api/version');
    expect(request.request.method).toBe('GET');
    request.flush({ version: '0.1.0+abc1234' });

    expect(await version).toEqual({ version: '0.1.0+abc1234' });
  });
});
