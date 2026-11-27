import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { components } from '../../api/schema';
import { somethingWentWrong } from '../../core/messages';
import { buttonLabelled as button } from '../../testing/button-labelled';
import { FakeActivityHub, provideFakeActivityHub } from '../../testing/fake-activity-hub';
import { text } from '../../testing/element-text';
import { epub, sendTwoFiles, uploadActivity as upload } from '../../testing/upload-fixtures';
import { UploadQueue } from '../upload/upload-queue';
import { UploadActivity } from './upload-activity';
import { UploadsSection } from './uploads-section';

type UploadActivityResponse = components['schemas']['UploadActivityResponse'];
type ProblemDetails = components['schemas']['ProblemDetails'];

describe('UploadsSection', () => {
  const workId = '0198a1b2-0000-7000-8000-0000000000aa';

  let httpTesting: HttpTestingController;
  let hub: FakeActivityHub;
  let queue: UploadQueue;
  let fixture: ComponentFixture<UploadsSection>;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        provideFakeActivityHub(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    hub = TestBed.inject(FakeActivityHub);
    queue = TestBed.inject(UploadQueue);
    fixture = TestBed.createComponent(UploadsSection);
    await fixture.whenStable();
  });

  afterEach(() => {
    httpTesting.verify({ ignoreCancelled: true });
  });

  /** Gives the section `uploads` from the server. */
  async function listUploads(uploads: UploadActivityResponse[]): Promise<void> {
    const activity = TestBed.inject(UploadActivity);
    activity.start();
    (await vi.waitFor(() => httpTesting.expectOne('/api/activity/uploads'))).flush(uploads);
    await vi.waitFor(() => expect(activity.rows()).toEqual(uploads));
    await fixture.whenStable();
  }

  /** The section as it stands once it has settled. */
  async function section(): Promise<HTMLElement> {
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  function rowsOf(section: HTMLElement): string[] {
    return Array.from(section.querySelectorAll<HTMLElement>('li')).map(text);
  }

  it('lists files waiting in the tab above the server uploads', async () => {
    await listUploads([upload(2), upload(1, 'added', { workId })]);
    sendTwoFiles(40);

    const uploads = await section();

    expect(rowsOf(uploads)).toEqual([
      expect.stringMatching(/^dune\.epubUploading 40%/),
      expect.stringMatching(/^emma\.epubWaiting/),
      expect.stringMatching(/^book-2\.epubQueued/),
      expect.stringMatching(/^book-1\.epubAdded/),
    ]);
  });

  it("shows failed uploads above the server's others and below this tab's files", async () => {
    await listUploads([
      upload(4),
      upload(3, 'failed', { failure: 'internal' }),
      upload(2, 'added', { workId }),
      upload(1, 'failed', { failure: 'unreadable' }),
    ]);
    sendTwoFiles(40);

    const uploads = await section();

    expect(rowsOf(uploads)).toEqual([
      expect.stringMatching(/^dune\.epub/),
      expect.stringMatching(/^emma\.epub/),
      expect.stringMatching(/^book-3\.epub/),
      expect.stringMatching(/^book-1\.epub/),
      expect.stringMatching(/^book-4\.epub/),
      expect.stringMatching(/^book-2\.epub/),
    ]);
  });

  it('shows the percentage of the file being sent', async () => {
    sendTwoFiles(40);

    const uploads = await section();

    expect(uploads.textContent).toContain('Uploading 40%');
    const bar = uploads.querySelector('[data-slot="progress"]');
    expect(bar?.getAttribute('aria-valuenow')).toBe('40');
  });

  it('links an added upload to its work', async () => {
    await listUploads([
      upload(1, 'added', { workId }),
      upload(2, 'duplicate', { workId }),
      upload(3, 'running'),
    ]);

    const uploads = await section();

    const links = Array.from(uploads.querySelectorAll('a')).map((link) => [
      link.textContent?.trim(),
      link.getAttribute('href'),
    ]);
    expect(links).toEqual([
      ['Added', `/works/${workId}`],
      ['Duplicate', `/works/${workId}`],
    ]);
    expect(uploads.textContent).toContain('Ingesting');
  });

  it('words a failure by its kind and never shows a raw error', async () => {
    await listUploads([
      upload(1, 'failed', { failure: 'unsupportedFormat' }),
      upload(2, 'failed', { failure: 'unreadable' }),
      upload(3, 'failed', { failure: 'internal' }),
      upload(4, 'failed', { failure: null }),
    ]);

    const uploads = await section();

    const [format, unreadable, internal, unknown] = rowsOf(uploads);
    expect(format).toContain('Failed');
    expect(format).toContain('not a supported book format');
    expect(unreadable).toContain('could not be read');
    expect(internal).toContain('went wrong');
    expect(unknown).toBe(internal.replace('book-3', 'book-4'));
    for (const row of [format, unreadable, internal, unknown]) {
      expect(row).toContain('Retry');
      expect(row).toContain('Remove');
    }
  });

  it('retries and removes a failed upload', async () => {
    const failed = upload(1, 'failed', { failure: 'internal' });
    await listUploads([failed]);
    const uploads = await section();

    button(uploads, 'Retry').click();
    const retry = await vi.waitFor(() =>
      httpTesting.expectOne(`/api/activity/uploads/${failed.jobId}/retry`),
    );
    expect(retry.request.method).toBe('POST');
    retry.flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();

    button(uploads, 'Remove').click();
    const remove = await vi.waitFor(() =>
      httpTesting.expectOne(`/api/activity/uploads/${failed.jobId}`),
    );
    expect(remove.request.method).toBe('DELETE');
    remove.flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();

    // The row changes only when the hub says so.
    expect(rowsOf(uploads)).toHaveLength(1);
    hub.uploadRemoved.next(failed.jobId);
    await fixture.whenStable();
    expect(rowsOf(uploads)).toEqual([]);
  });

  it('says so on the row when a retry fails', async () => {
    const failed = upload(1, 'failed', { failure: 'internal' });
    await listUploads([failed]);
    const uploads = await section();

    button(uploads, 'Retry').click();
    (
      await vi.waitFor(() => httpTesting.expectOne(`/api/activity/uploads/${failed.jobId}/retry`))
    ).flush({ title: 'Server Error', status: 500 } satisfies ProblemDetails, {
      status: 500,
      statusText: 'Server Error',
    });
    await fixture.whenStable();

    await vi.waitFor(() => expect(rowsOf(uploads)[0]).toContain(somethingWentWrong));
  });

  it('still says a retry failed when the answer came while the section was away', async () => {
    const failed = upload(1, 'failed', { failure: 'internal' });
    await listUploads([failed]);
    button(await section(), 'Retry').click();
    const retry = await vi.waitFor(() =>
      httpTesting.expectOne(`/api/activity/uploads/${failed.jobId}/retry`),
    );

    // The popover makes the section anew each time it opens.
    fixture.destroy();
    retry.flush({ title: 'Server Error', status: 500 } satisfies ProblemDetails, {
      status: 500,
      statusText: 'Server Error',
    });
    fixture = TestBed.createComponent(UploadsSection);

    await vi.waitFor(async () => expect(rowsOf(await section())[0]).toContain(somethingWentWrong));
  });

  it('retries and removes a file the server refused in this tab', async () => {
    queue.add([epub('dune.epub')], null);
    httpTesting
      .expectOne('/api/uploads')
      .flush(
        { title: 'Payload Too Large', status: 413, detail: 'Too big for this library.' },
        { status: 413, statusText: 'Payload Too Large' },
      );
    await fixture.whenStable();
    const uploads = await section();

    expect(rowsOf(uploads)[0]).toContain('Failed');
    expect(rowsOf(uploads)[0]).toContain('Too big for this library.');

    button(uploads, 'Retry').click();
    expect(queue.rows()[0].status).toBe('uploading');
    httpTesting
      .expectOne('/api/uploads')
      .flush(
        { title: 'Payload Too Large', status: 413, detail: 'Too big for this library.' },
        { status: 413, statusText: 'Payload Too Large' },
      );
    await fixture.whenStable();

    button(uploads, 'Remove').click();
    expect(queue.rows()).toEqual([]);
  });
});
