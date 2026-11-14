import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { IngestResult } from '../../api/jobs-api';
import { components } from '../../api/schema';
import { somethingWentWrong } from '../../core/messages';
import { UploadQueue } from './upload-queue';

type JobDetailResponse = components['schemas']['JobDetailResponse'];
type ProblemDetails = components['schemas']['ProblemDetails'];
type UploadResponse = components['schemas']['UploadResponse'];

describe('UploadQueue', () => {
  const libraryId = '0198a1b2-0000-7000-8000-00000000000a';
  const duneJobId = '0198a1b2-0000-7000-8000-0000000000d1';
  const emmaJobId = '0198a1b2-0000-7000-8000-0000000000d2';

  let queue: UploadQueue;
  let httpTesting: HttpTestingController;
  let ignoreCancelled: boolean;

  beforeEach(() => {
    ignoreCancelled = false;
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    queue = TestBed.inject(UploadQueue);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify({ ignoreCancelled }));

  function epub(name: string): File {
    return new File(['epub bytes'], name, { type: 'application/epub+zip' });
  }

  /** The one open upload, after checking it sends `file` to the library. */
  function openUpload(file: File) {
    const request = httpTesting.expectOne('/api/uploads');
    expect(request.request.method).toBe('POST');
    const form = request.request.body as FormData;
    expect(form.get('libraryId')).toBe(libraryId);
    expect(form.get('file')).toBe(file);
    return request;
  }

  function accept(file: File, jobId: string): void {
    const response: UploadResponse = { jobId };
    openUpload(file).flush(response, { status: 202, statusText: 'Accepted' });
  }

  function statuses() {
    return queue.rows().map((row) => [row.name, row.status]);
  }

  it('sends files one request at a time in the order they were added', () => {
    const dune = epub('dune.epub');
    const emma = epub('emma.epub');

    queue.add([dune, emma], libraryId);

    expect(statuses()).toEqual([
      ['dune.epub', 'uploading'],
      ['emma.epub', 'waiting'],
    ]);
    accept(dune, duneJobId);

    expect(statuses()).toEqual([
      ['dune.epub', 'ingesting'],
      ['emma.epub', 'uploading'],
    ]);
    expect(queue.rows()[0].jobId).toBe(duneJobId);
    accept(emma, emmaJobId);

    expect(statuses()).toEqual([
      ['dune.epub', 'ingesting'],
      ['emma.epub', 'ingesting'],
    ]);
    expect(queue.rows()[1].jobId).toBe(emmaJobId);
  });

  it("a refused upload fails its row with the server's message and the next file is still sent", () => {
    const huge = epub('huge.epub');
    const emma = epub('emma.epub');
    queue.add([huge, emma], libraryId);

    const problem: ProblemDetails = {
      title: 'Payload Too Large',
      status: 413,
      detail: 'The file is larger than the 200 MB limit.',
    };
    openUpload(huge).flush(problem, { status: 413, statusText: 'Payload Too Large' });

    expect(statuses()).toEqual([
      ['huge.epub', 'failed'],
      ['emma.epub', 'uploading'],
    ]);
    expect(queue.rows()[0].error).toBe('The file is larger than the 200 MB limit.');
    accept(emma, emmaJobId);
  });

  it('a refusal with only a title fails its row with the title', () => {
    const dune = epub('dune.epub');
    queue.add([dune], libraryId);

    const problem: ProblemDetails = { title: 'The library does not exist.', status: 404 };
    openUpload(dune).flush(problem, { status: 404, statusText: 'Not Found' });

    expect(statuses()).toEqual([['dune.epub', 'failed']]);
    expect(queue.rows()[0].error).toBe('The library does not exist.');
  });

  it('a refusal with no message fails its row with the general message', () => {
    const dune = epub('dune.epub');
    queue.add([dune], libraryId);

    openUpload(dune).flush('Bad gateway', { status: 502, statusText: 'Bad Gateway' });

    expect(statuses()).toEqual([['dune.epub', 'failed']]);
    expect(queue.rows()[0].error).toBe(somethingWentWrong);
  });

  it('files added while one is sending wait their turn', () => {
    const dune = epub('dune.epub');
    const emma = epub('emma.epub');
    queue.add([dune], libraryId);
    const request = openUpload(dune);

    queue.add([emma], libraryId);

    expect(statuses()).toEqual([
      ['dune.epub', 'uploading'],
      ['emma.epub', 'waiting'],
    ]);
    request.flush({ jobId: duneJobId } satisfies UploadResponse);
    accept(emma, emmaJobId);
  });

  it('a destroyed queue cancels its open upload', () => {
    queue.add([epub('dune.epub')], libraryId);
    const request = httpTesting.expectOne('/api/uploads');

    TestBed.resetTestingModule();

    ignoreCancelled = true;
    expect(request.cancelled).toBe(true);
  });

  /** Whether leaving the page now would be questioned. */
  function leavingIsQuestioned(): boolean {
    const event = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(event);
    return event.defaultPrevented;
  }

  it('leaving is questioned while a file is waiting or uploading', () => {
    const dune = epub('dune.epub');
    const emma = epub('emma.epub');
    queue.add([dune, emma], libraryId);
    expect(leavingIsQuestioned()).toBe(true);

    accept(dune, duneJobId);
    expect(statuses()).toEqual([
      ['dune.epub', 'ingesting'],
      ['emma.epub', 'uploading'],
    ]);
    expect(leavingIsQuestioned()).toBe(true);

    accept(emma, emmaJobId);
  });

  it('leaving is not questioned with nothing added', () => {
    expect(leavingIsQuestioned()).toBe(false);
  });

  it('a destroyed queue no longer questions leaving', () => {
    queue.add([epub('dune.epub')], libraryId);
    const request = httpTesting.expectOne('/api/uploads');

    TestBed.resetTestingModule();

    ignoreCancelled = true;
    expect(request.cancelled).toBe(true);
    expect(leavingIsQuestioned()).toBe(false);
  });

  describe('following jobs', () => {
    const workId = '0198a1b2-0000-7000-8000-000000000001';
    const itemId = '0198a1b2-0000-7000-8000-0000000000e1';

    beforeEach(() => vi.useFakeTimers());

    afterEach(() => vi.useRealTimers());

    function job(
      id: string,
      status: JobDetailResponse['status'],
      fields: Partial<JobDetailResponse> = {},
    ): JobDetailResponse {
      return {
        id,
        type: 'ingest',
        status,
        error: null,
        parentJobId: null,
        notBefore: null,
        attemptsMade: 1,
        attemptsAllowed: 3,
        reclaimsMade: 0,
        createdAt: '2026-11-14T12:00:00Z',
        updatedAt: '2026-11-14T12:00:01Z',
        result: null,
        steps: [],
        children: [],
        ...fields,
      };
    }

    /** Lets a second pass, then answers the one poll of `jobId` with `body` and lets the answer land. */
    async function answerPoll(
      jobId: string,
      body: JobDetailResponse | ProblemDetails | string,
      status = 200,
    ): Promise<void> {
      await vi.advanceTimersByTimeAsync(1000);
      const request = httpTesting.expectOne(`/api/jobs/${jobId}`);
      expect(request.request.method).toBe('GET');
      request.flush(body, { status, statusText: status === 200 ? 'OK' : 'Error' });
      await vi.advanceTimersByTimeAsync(0);
    }

    function ingest(name: string, jobId: string): void {
      const file = epub(name);
      queue.add([file], libraryId);
      accept(file, jobId);
    }

    it('a row stays ingesting while its job is pending or running', async () => {
      ingest('dune.epub', duneJobId);

      await answerPoll(duneJobId, job(duneJobId, 'pending'));
      expect(statuses()).toEqual([['dune.epub', 'ingesting']]);

      await answerPoll(duneJobId, job(duneJobId, 'running'));
      expect(statuses()).toEqual([['dune.epub', 'ingesting']]);
    });

    it("a job that created an item makes its row added with the work's id", async () => {
      ingest('dune.epub', duneJobId);

      const result: IngestResult = { outcome: 'created', itemId, workId };
      await answerPoll(duneJobId, job(duneJobId, 'succeeded', { result }));

      expect(statuses()).toEqual([['dune.epub', 'added']]);
      expect(queue.rows()[0].workId).toBe(workId);
    });

    it("a job that found a duplicate makes its row duplicate with the work's id", async () => {
      ingest('dune.epub', duneJobId);

      const result: IngestResult = { outcome: 'duplicate', itemId, workId };
      await answerPoll(duneJobId, job(duneJobId, 'succeeded', { result }));

      expect(statuses()).toEqual([['dune.epub', 'duplicate']]);
      expect(queue.rows()[0].workId).toBe(workId);
    });

    it("a failed job fails its row with the job's error", async () => {
      ingest('dune.epub', duneJobId);

      await answerPoll(duneJobId, job(duneJobId, 'failed', { error: 'The file is not an EPUB.' }));

      expect(statuses()).toEqual([['dune.epub', 'failed']]);
      expect(queue.rows()[0].error).toBe('The file is not an EPUB.');
    });

    it('a poll that fails is tried again a second later', async () => {
      ingest('dune.epub', duneJobId);

      await answerPoll(duneJobId, 'Bad gateway', 502);
      expect(statuses()).toEqual([['dune.epub', 'ingesting']]);

      const result: IngestResult = { outcome: 'created', itemId, workId };
      await answerPoll(duneJobId, job(duneJobId, 'succeeded', { result }));
      expect(statuses()).toEqual([['dune.epub', 'added']]);
    });

    it('a job that no longer exists fails its row', async () => {
      ingest('dune.epub', duneJobId);

      const problem: ProblemDetails = { title: 'Not Found', status: 404 };
      await answerPoll(duneJobId, problem, 404);

      expect(statuses()).toEqual([['dune.epub', 'failed']]);
      expect(queue.rows()[0].error).toBe('The job no longer exists.');
    });

    it('every ingesting row is polled each round', async () => {
      ingest('dune.epub', duneJobId);
      ingest('emma.epub', emmaJobId);

      await vi.advanceTimersByTimeAsync(1000);
      httpTesting.expectOne(`/api/jobs/${duneJobId}`).flush(job(duneJobId, 'running'));
      httpTesting
        .expectOne(`/api/jobs/${emmaJobId}`)
        .flush(job(emmaJobId, 'failed', { error: 'Broken.' }));
      await vi.advanceTimersByTimeAsync(0);

      expect(statuses()).toEqual([
        ['dune.epub', 'ingesting'],
        ['emma.epub', 'failed'],
      ]);
      await answerPoll(duneJobId, job(duneJobId, 'failed', { error: 'Broken.' }));
    });

    it('nothing is polled once no row is ingesting', async () => {
      ingest('dune.epub', duneJobId);
      await answerPoll(duneJobId, job(duneJobId, 'failed', { error: 'Broken.' }));

      await vi.advanceTimersByTimeAsync(5000);

      httpTesting.expectNone(() => true);
    });

    async function failedJob(name: string, jobId: string): Promise<void> {
      ingest(name, jobId);
      await answerPoll(jobId, job(jobId, 'failed', { error: 'Broken.' }));
    }

    function failedUpload(name: string): File {
      const file = epub(name);
      queue.add([file], libraryId);
      openUpload(file).flush('Bad gateway', { status: 502, statusText: 'Bad Gateway' });
      return file;
    }

    /** Answers the one open request to `url` made with `method`, and lets the answer land. */
    async function answer(method: string, url: string, status: number): Promise<void> {
      const request = httpTesting.expectOne({ method, url });
      const problem: ProblemDetails = { status };
      request.flush(status === 204 ? null : problem, { status, statusText: String(status) });
      await vi.advanceTimersByTimeAsync(0);
    }

    it('retrying a row whose job failed reruns the job and shows it ingesting', async () => {
      await failedJob('dune.epub', duneJobId);

      queue.retry(queue.rows()[0]);
      await answer('POST', `/api/jobs/${duneJobId}/retry`, 204);

      expect(statuses()).toEqual([['dune.epub', 'ingesting']]);
      expect(queue.rows()[0].error).toBeUndefined();
      const result: IngestResult = { outcome: 'created', itemId, workId };
      await answerPoll(duneJobId, job(duneJobId, 'succeeded', { result }));
      expect(statuses()).toEqual([['dune.epub', 'added']]);
    });

    it('retrying a job that is no longer failed goes back to following it', async () => {
      await failedJob('dune.epub', duneJobId);

      queue.retry(queue.rows()[0]);
      await answer('POST', `/api/jobs/${duneJobId}/retry`, 409);

      expect(statuses()).toEqual([['dune.epub', 'ingesting']]);
      await answerPoll(duneJobId, job(duneJobId, 'running'));
      await answerPoll(duneJobId, job(duneJobId, 'failed', { error: 'Broken again.' }));
      expect(queue.rows()[0].error).toBe('Broken again.');
    });

    it('retrying a row whose upload failed sends the file again', async () => {
      const dune = failedUpload('dune.epub');

      queue.retry(queue.rows()[0]);

      expect(statuses()).toEqual([['dune.epub', 'uploading']]);
      expect(queue.rows()[0].error).toBeUndefined();
      accept(dune, duneJobId);
      expect(statuses()).toEqual([['dune.epub', 'ingesting']]);
      await answerPoll(duneJobId, job(duneJobId, 'failed', { error: 'Broken.' }));
    });

    it('removing a row whose job failed deletes the job and drops the row', async () => {
      await failedJob('dune.epub', duneJobId);
      failedUpload('emma.epub');

      queue.remove(queue.rows()[0]);
      expect(statuses()).toEqual([
        ['dune.epub', 'failed'],
        ['emma.epub', 'failed'],
      ]);
      await answer('DELETE', `/api/jobs/${duneJobId}`, 204);

      expect(statuses()).toEqual([['emma.epub', 'failed']]);
    });

    it('removing a row whose job is already gone drops the row', async () => {
      await failedJob('dune.epub', duneJobId);

      queue.remove(queue.rows()[0]);
      await answer('DELETE', `/api/jobs/${duneJobId}`, 404);

      expect(statuses()).toEqual([]);
    });

    it('removing a row whose upload failed drops the row without a request', () => {
      failedUpload('dune.epub');

      queue.remove(queue.rows()[0]);

      expect(statuses()).toEqual([]);
      httpTesting.expectNone(() => true);
    });

    it('a retry or removal the server fails leaves the row failed with the general message', async () => {
      await failedJob('dune.epub', duneJobId);

      queue.retry(queue.rows()[0]);
      await answer('POST', `/api/jobs/${duneJobId}/retry`, 500);
      expect(statuses()).toEqual([['dune.epub', 'failed']]);
      expect(queue.rows()[0].error).toBe(somethingWentWrong);

      queue.remove(queue.rows()[0]);
      await answer('DELETE', `/api/jobs/${duneJobId}`, 500);
      expect(statuses()).toEqual([['dune.epub', 'failed']]);
      expect(queue.rows()[0].error).toBe(somethingWentWrong);
    });

    it('clearing finished rows keeps waiting, uploading, ingesting and failed rows', async () => {
      const result = (outcome: IngestResult['outcome']): IngestResult => ({
        outcome,
        itemId,
        workId,
      });
      const addedJobId = '0198a1b2-0000-7000-8000-0000000000d3';
      const duplicateJobId = '0198a1b2-0000-7000-8000-0000000000d4';
      ingest('added.epub', addedJobId);
      ingest('duplicate.epub', duplicateJobId);
      failedUpload('failed.epub');
      ingest('ingesting.epub', duneJobId);
      await vi.advanceTimersByTimeAsync(1000);
      httpTesting
        .expectOne(`/api/jobs/${addedJobId}`)
        .flush(job(addedJobId, 'succeeded', { result: result('created') }));
      httpTesting
        .expectOne(`/api/jobs/${duplicateJobId}`)
        .flush(job(duplicateJobId, 'succeeded', { result: result('duplicate') }));
      httpTesting.expectOne(`/api/jobs/${duneJobId}`).flush(job(duneJobId, 'running'));
      await vi.advanceTimersByTimeAsync(0);
      const uploading = epub('uploading.epub');
      const waiting = epub('waiting.epub');
      queue.add([uploading, waiting], libraryId);

      queue.clearFinished();

      expect(statuses()).toEqual([
        ['failed.epub', 'failed'],
        ['ingesting.epub', 'ingesting'],
        ['uploading.epub', 'uploading'],
        ['waiting.epub', 'waiting'],
      ]);
      queue.reset();
      ignoreCancelled = true;
    });

    it('leaving is not questioned once every file is sent', async () => {
      ingest('ingesting.epub', duneJobId);
      ingest('added.epub', emmaJobId);
      failedUpload('failed.epub');
      await vi.advanceTimersByTimeAsync(1000);
      httpTesting.expectOne(`/api/jobs/${duneJobId}`).flush(job(duneJobId, 'running'));
      const result: IngestResult = { outcome: 'created', itemId, workId };
      httpTesting
        .expectOne(`/api/jobs/${emmaJobId}`)
        .flush(job(emmaJobId, 'succeeded', { result }));
      await vi.advanceTimersByTimeAsync(0);

      expect(statuses()).toEqual([
        ['ingesting.epub', 'ingesting'],
        ['added.epub', 'added'],
        ['failed.epub', 'failed'],
      ]);
      expect(leavingIsQuestioned()).toBe(false);
    });

    it('resetting drops every row, cancels the open upload and sends nothing more', async () => {
      ingest('dune.epub', duneJobId);
      queue.add([epub('emma.epub'), epub('huck.epub')], libraryId);
      const open = httpTesting.expectOne('/api/uploads');

      queue.reset();

      ignoreCancelled = true;
      expect(statuses()).toEqual([]);
      expect(open.cancelled).toBe(true);
      await vi.advanceTimersByTimeAsync(5000);
      httpTesting.expectNone('/api/uploads');
      httpTesting.expectNone(() => true);
    });
  });
});
