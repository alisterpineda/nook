import { HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { components } from './schema';
import { UploadEvent, UploadsApi } from './uploads-api';

type UploadResponse = components['schemas']['UploadResponse'];
type UploadLimitsResponse = components['schemas']['UploadLimitsResponse'];

describe('UploadsApi', () => {
  let api: UploadsApi;
  let httpTesting: HttpTestingController;

  const sectionId = '0198a1b2-0000-7000-8000-00000000000a';

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(UploadsApi);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  it('upload posts the section id and the file as a multipart form', () => {
    const file = new File(['epub bytes'], 'dune.epub', { type: 'application/epub+zip' });
    const response: UploadResponse = { jobId: '0198a1b2-0000-7000-8000-0000000000d1' };
    const answers: UploadEvent[] = [];

    api.upload(sectionId, file).subscribe((answer) => answers.push(answer));
    const request = httpTesting.expectOne('/api/uploads');
    expect(request.request.method).toBe('POST');
    const body = request.request.body as unknown;
    expect(body).toBeInstanceOf(FormData);
    const form = body as FormData;
    expect(form.get('sectionId')).toBe(sectionId);
    expect(form.get('file')).toBe(file);
    request.flush(response, { status: 202, statusText: 'Accepted' });

    expect(answers).toEqual([{ kind: 'accepted', jobId: response.jobId }]);
    httpTesting.verify();
  });

  it('upload leaves the section out of the form when there is none', () => {
    const file = new File(['epub bytes'], 'dune.epub', { type: 'application/epub+zip' });

    api.upload(null, file).subscribe();
    const request = httpTesting.expectOne('/api/uploads');
    const form = request.request.body as FormData;
    expect(form.get('file')).toBe(file);
    expect(form.has('sectionId')).toBe(false);
    request.flush(
      { jobId: '0198a1b2-0000-7000-8000-0000000000d1' },
      { status: 202, statusText: 'Accepted' },
    );

    httpTesting.verify();
  });

  it('unsubscribing from an upload cancels its request', () => {
    const file = new File(['epub bytes'], 'dune.epub', { type: 'application/epub+zip' });

    const subscription = api.upload(sectionId, file).subscribe();
    const request = httpTesting.expectOne('/api/uploads');
    expect(request.cancelled).toBe(false);
    subscription.unsubscribe();

    expect(request.cancelled).toBe(true);
    httpTesting.verify({ ignoreCancelled: true });
  });

  it('reports how much of the file has been sent, then the job', () => {
    const file = new File(['epub bytes'], 'dune.epub', { type: 'application/epub+zip' });
    const events: UploadEvent[] = [];

    api.upload(sectionId, file).subscribe((event) => events.push(event));
    const request = httpTesting.expectOne('/api/uploads');
    expect(request.request.reportProgress).toBe(true);
    request.event({ type: HttpEventType.UploadProgress, loaded: 40, total: 100 });
    request.event({ type: HttpEventType.UploadProgress, loaded: 100, total: 100 });
    request.flush(
      { jobId: '0198a1b2-0000-7000-8000-0000000000d1' },
      { status: 202, statusText: 'Accepted' },
    );

    expect(events).toEqual([
      { kind: 'progress', percent: 40 },
      { kind: 'progress', percent: 100 },
      { kind: 'accepted', jobId: '0198a1b2-0000-7000-8000-0000000000d1' },
    ]);
    httpTesting.verify();
  });

  it('asks for the size limit', async () => {
    const limits: UploadLimitsResponse = { maxBytes: 1024 };

    const asking = api.limits();
    const request = httpTesting.expectOne('/api/uploads/limits');
    expect(request.request.method).toBe('GET');
    request.flush(limits);

    expect(await asking).toEqual(limits);
    httpTesting.verify();
  });
});
