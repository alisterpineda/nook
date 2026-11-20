import { HttpEventType, provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { components } from '../../api/schema';
import { somethingWentWrong } from '../../core/messages';
import { epub } from '../../testing/upload-fixtures';
import { UploadQueue } from './upload-queue';

type ProblemDetails = components['schemas']['ProblemDetails'];
type UploadResponse = components['schemas']['UploadResponse'];

describe('UploadQueue', () => {
  const sectionId = '0198a1b2-0000-7000-8000-00000000000a';
  const duneJobId = '0198a1b2-0000-7000-8000-0000000000d1';
  const emmaJobId = '0198a1b2-0000-7000-8000-0000000000d2';

  let queue: UploadQueue;
  let httpTesting: HttpTestingController;
  let ignoreCancelled: boolean;

  beforeEach(() => {
    ignoreCancelled = false;
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withXhr()), provideHttpClientTesting()],
    });
    queue = TestBed.inject(UploadQueue);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify({ ignoreCancelled }));

  /** The one open upload, after checking it sends `file` to the section. */
  function openUpload(file: File) {
    const request = httpTesting.expectOne('/api/uploads');
    expect(request.request.method).toBe('POST');
    const form = request.request.body as FormData;
    expect(form.get('sectionId')).toBe(sectionId);
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

  function refuse(file: File): void {
    const problem: ProblemDetails = {
      title: 'Payload Too Large',
      status: 413,
      detail: 'The file is larger than the 200 MB limit.',
    };
    openUpload(file).flush(problem, { status: 413, statusText: 'Payload Too Large' });
  }

  it('sends files one at a time in the order added', () => {
    const dune = epub('dune.epub');
    const emma = epub('emma.epub');

    queue.add([dune, emma], sectionId);

    expect(statuses()).toEqual([
      ['dune.epub', 'uploading'],
      ['emma.epub', 'waiting'],
    ]);
    accept(dune, duneJobId);

    expect(statuses()).toEqual([['emma.epub', 'uploading']]);
    accept(emma, emmaJobId);

    expect(statuses()).toEqual([]);
  });

  it('shows how much of the sending file has gone', () => {
    const dune = epub('dune.epub');
    queue.add([dune], sectionId);
    const request = openUpload(dune);

    request.event({ type: HttpEventType.UploadProgress, loaded: 40, total: 100 });
    expect(queue.rows()[0].percent).toBe(40);

    request.event({ type: HttpEventType.UploadProgress, loaded: 90, total: 100 });
    expect(queue.rows()[0].percent).toBe(90);
    request.flush({ jobId: duneJobId } satisfies UploadResponse);
  });

  it('drops a file from the queue once the server has taken it', () => {
    const dune = epub('dune.epub');
    queue.add([dune], sectionId);

    accept(dune, duneJobId);

    expect(queue.rows()).toEqual([]);
  });

  it("keeps a refused file with the server's reason, and the next file is still sent", () => {
    const huge = epub('huge.epub');
    const emma = epub('emma.epub');
    queue.add([huge, emma], sectionId);

    refuse(huge);

    expect(statuses()).toEqual([
      ['huge.epub', 'failed'],
      ['emma.epub', 'uploading'],
    ]);
    expect(queue.rows()[0].error).toBe('The file is larger than the 200 MB limit.');
    accept(emma, emmaJobId);
  });

  it('a refusal with only a title keeps the title, and one with no message keeps the general message', () => {
    const dune = epub('dune.epub');
    const emma = epub('emma.epub');
    queue.add([dune, emma], sectionId);

    const problem: ProblemDetails = { title: 'The section does not exist.', status: 404 };
    openUpload(dune).flush(problem, { status: 404, statusText: 'Not Found' });
    openUpload(emma).flush('Bad gateway', { status: 502, statusText: 'Bad Gateway' });

    expect(queue.rows().map((row) => row.error)).toEqual([
      'The section does not exist.',
      somethingWentWrong,
    ]);
  });

  it('sends a refused file again on retry', () => {
    const huge = epub('huge.epub');
    queue.add([huge], sectionId);
    refuse(huge);

    queue.retry(queue.rows()[0].id);

    expect(statuses()).toEqual([['huge.epub', 'uploading']]);
    expect(queue.rows()[0].error).toBeUndefined();
    accept(huge, duneJobId);
    expect(queue.rows()).toEqual([]);
  });

  it('retry leaves a row that has not failed alone', () => {
    const dune = epub('dune.epub');
    queue.add([dune], sectionId);

    queue.retry(queue.rows()[0].id);

    expect(statuses()).toEqual([['dune.epub', 'uploading']]);
    accept(dune, duneJobId);
  });

  it('drops a refused file on remove', () => {
    const huge = epub('huge.epub');
    queue.add([huge], sectionId);
    refuse(huge);

    queue.remove(queue.rows()[0].id);

    expect(queue.rows()).toEqual([]);
    httpTesting.expectNone(() => true);
  });

  it('files added while one is sending wait their turn', () => {
    const dune = epub('dune.epub');
    const emma = epub('emma.epub');
    queue.add([dune], sectionId);
    const request = openUpload(dune);

    queue.add([emma], sectionId);

    expect(statuses()).toEqual([
      ['dune.epub', 'uploading'],
      ['emma.epub', 'waiting'],
    ]);
    request.flush({ jobId: duneJobId } satisfies UploadResponse);
    accept(emma, emmaJobId);
  });

  it('a destroyed queue cancels its open upload', () => {
    queue.add([epub('dune.epub')], sectionId);
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
    queue.add([dune, emma], sectionId);
    expect(leavingIsQuestioned()).toBe(true);

    accept(dune, duneJobId);
    expect(leavingIsQuestioned()).toBe(true);

    accept(emma, emmaJobId);
    expect(leavingIsQuestioned()).toBe(false);
  });

  it('leaving is not questioned with nothing added or only a refused file left', () => {
    expect(leavingIsQuestioned()).toBe(false);

    const huge = epub('huge.epub');
    queue.add([huge], sectionId);
    refuse(huge);
    expect(leavingIsQuestioned()).toBe(false);
  });

  it('a destroyed queue no longer questions leaving', () => {
    queue.add([epub('dune.epub')], sectionId);
    const request = httpTesting.expectOne('/api/uploads');

    TestBed.resetTestingModule();

    ignoreCancelled = true;
    expect(request.cancelled).toBe(true);
    expect(leavingIsQuestioned()).toBe(false);
  });

  it('resetting drops every row, cancels the open upload and sends nothing more', () => {
    queue.add([epub('dune.epub'), epub('emma.epub')], sectionId);
    const open = httpTesting.expectOne('/api/uploads');

    queue.reset();

    ignoreCancelled = true;
    expect(queue.rows()).toEqual([]);
    expect(open.cancelled).toBe(true);
    httpTesting.expectNone(() => true);
  });
});
