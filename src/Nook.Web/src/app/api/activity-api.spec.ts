import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivityApi } from './activity-api';
import { components } from './schema';

type UploadActivityResponse = components['schemas']['UploadActivityResponse'];

describe('ActivityApi', () => {
  let api: ActivityApi;
  let httpTesting: HttpTestingController;

  const awkwardId = 'job/1?x';
  const encodedId = 'job%2F1%3Fx';

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(ActivityApi);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it("lists the user's uploads", async () => {
    const uploads: UploadActivityResponse[] = [
      {
        jobId: '0198a1b2-0000-7000-8000-0000000000d1',
        fileName: 'dune.epub',
        sectionId: null,
        sectionName: null,
        submittedAt: '2026-11-20T12:00:00Z',
        status: 'queued',
        workId: null,
        failure: null,
      },
    ];

    const listing = api.uploads();
    const request = httpTesting.expectOne('/api/activity/uploads');
    expect(request.request.method).toBe('GET');
    request.flush(uploads);

    expect(await listing).toEqual(uploads);
  });

  it('retries an upload', async () => {
    const retrying = api.retry(awkwardId);
    const request = httpTesting.expectOne(`/api/activity/uploads/${encodedId}/retry`);
    expect(request.request.method).toBe('POST');
    request.flush(null, { status: 204, statusText: 'No Content' });

    await expect(retrying).resolves.toBeUndefined();
  });

  it('removes an upload', async () => {
    const removing = api.remove(awkwardId);
    const request = httpTesting.expectOne(`/api/activity/uploads/${encodedId}`);
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });

    await expect(removing).resolves.toBeUndefined();
  });
});
