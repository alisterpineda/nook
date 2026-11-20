import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { JobsApi } from './jobs-api';
import { components } from './schema';

type JobDetailResponse = components['schemas']['JobDetailResponse'];

describe('JobsApi', () => {
  let api: JobsApi;
  let httpTesting: HttpTestingController;

  const awkwardId = 'job/1?x';
  const encodedId = 'job%2F1%3Fx';

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(JobsApi);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('get asks for one job', async () => {
    const result = {
      outcome: 'created',
      itemId: '0198a1b2-0000-7000-8000-0000000000e1',
      workId: '0198a1b2-0000-7000-8000-000000000001',
    };
    const job: JobDetailResponse = {
      id: '0198a1b2-0000-7000-8000-0000000000d1',
      type: 'ingest',
      status: 'succeeded',
      error: null,
      parentJobId: null,
      notBefore: null,
      attemptsMade: 1,
      attemptsAllowed: 3,
      reclaimsMade: 0,
      createdAt: '2026-11-14T12:00:00Z',
      updatedAt: '2026-11-14T12:00:01Z',
      result,
      steps: [],
      children: [],
    };

    const fetching = api.get(awkwardId);
    const request = httpTesting.expectOne(`/api/jobs/${encodedId}`);
    expect(request.request.method).toBe('GET');
    request.flush(job);

    expect(await fetching).toEqual(job);
  });

  it("retry posts to the job's retry", async () => {
    const retrying = api.retry(awkwardId);
    const request = httpTesting.expectOne(`/api/jobs/${encodedId}/retry`);
    expect(request.request.method).toBe('POST');
    request.flush(null, { status: 204, statusText: 'No Content' });

    await expect(retrying).resolves.toBeUndefined();
  });

  it('delete deletes the job', async () => {
    const deleting = api.delete(awkwardId);
    const request = httpTesting.expectOne(`/api/jobs/${encodedId}`);
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });

    await expect(deleting).resolves.toBeUndefined();
  });
});
