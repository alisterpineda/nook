import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PublicAccessApi } from './public-access-api';
import { components } from './schema';

type PublicAccessRequest = components['schemas']['PublicAccessRequest'];
type PublicAccessResponse = components['schemas']['PublicAccessResponse'];

describe('PublicAccessApi', () => {
  let api: PublicAccessApi;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(PublicAccessApi);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('reads the public access settings', async () => {
    const settings: PublicAccessResponse = {
      enabled: true,
      access: 'read',
      defaultAreaAccess: 'none',
      sections: [
        { id: '0198a1b2-0000-7000-8000-00000000000a', name: 'Books', access: 'read' },
        { id: '0198a1b2-0000-7000-8000-00000000000b', name: 'Comics', access: 'none' },
      ],
    };

    const reading = api.get();
    const request = httpTesting.expectOne('/api/public-access');
    expect(request.request.method).toBe('GET');
    request.flush(settings);

    expect(await reading).toEqual(settings);
  });

  it('saves the public access settings', async () => {
    const settings: PublicAccessRequest = {
      enabled: true,
      access: 'download',
      defaultAreaAccess: 'browse',
      sections: [{ id: '0198a1b2-0000-7000-8000-00000000000a', access: 'read' }],
    };

    const saving = api.save(settings);
    const request = httpTesting.expectOne('/api/public-access');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(settings);
    request.flush(null, { status: 204, statusText: 'No Content' });

    await expect(saving).resolves.toBeUndefined();
  });
});
