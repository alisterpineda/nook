import { HttpEventType, provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { components } from '../../api/schema';
import { somethingWentWrong } from '../../core/messages';
import { buttonLabelled as button } from '../../testing/button-labelled';
import { FakeActivityHub, provideFakeActivityHub } from '../../testing/fake-activity-hub';
import { epub, uploadActivity as upload } from '../../testing/upload-fixtures';
import { UploadQueue } from '../upload/upload-queue';
import { UploadActivity } from './upload-activity';
import { ActivityButton } from './activity-button';

type UploadActivityResponse = components['schemas']['UploadActivityResponse'];
type ProblemDetails = components['schemas']['ProblemDetails'];

describe('ActivityButton', () => {
  const workId = '0198a1b2-0000-7000-8000-0000000000aa';

  let httpTesting: HttpTestingController;
  let hub: FakeActivityHub;
  let queue: UploadQueue;
  let fixture: ComponentFixture<ActivityButton>;

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
    fixture = TestBed.createComponent(ActivityButton);
    await fixture.whenStable();
  });

  afterEach(() => {
    // Close any open popover so the next test starts with a clean page.
    fixture.destroy();
    httpTesting.verify({ ignoreCancelled: true });
  });

  /** Gives the Activity list `uploads` from the server. */
  async function listUploads(uploads: UploadActivityResponse[]): Promise<void> {
    const activity = TestBed.inject(UploadActivity);
    activity.start();
    (await vi.waitFor(() => httpTesting.expectOne('/api/activity/uploads'))).flush(uploads);
    await vi.waitFor(() => expect(activity.rows()).toEqual(uploads));
    await fixture.whenStable();
  }

  async function openPopover(): Promise<HTMLElement> {
    trigger().click();
    await fixture.whenStable();
    return vi.waitFor(() => {
      const content = document.querySelector<HTMLElement>('[data-slot="popover-content"]');
      if (!content) {
        throw new Error('The popover is not open.');
      }
      return content;
    });
  }

  function trigger(): HTMLButtonElement {
    return (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '[data-slot="popover-trigger"]',
    )!;
  }

  function badge(): HTMLElement | null {
    return trigger().querySelector<HTMLElement>('[data-slot="badge"]');
  }

  function rowsOf(popover: HTMLElement): string[] {
    return Array.from(popover.querySelectorAll<HTMLElement>('li')).map((row) =>
      (row.textContent ?? '').replace(/\s+/g, ' ').trim(),
    );
  }

  /** Adds two files, the first of which is being sent at `percent`. */
  function sendTwoFiles(percent: number): void {
    queue.add([epub('dune.epub'), epub('emma.epub')], null);
    httpTesting.expectOne('/api/uploads').event({
      type: HttpEventType.UploadProgress,
      loaded: percent,
      total: 100,
    });
  }

  it('lists files waiting in the tab above the server uploads', async () => {
    await listUploads([upload(2), upload(1, 'added', { workId })]);
    sendTwoFiles(40);

    const popover = await openPopover();

    expect(rowsOf(popover)).toEqual([
      expect.stringMatching(/^dune\.epubUploading 40%/),
      expect.stringMatching(/^emma\.epubWaiting/),
      expect.stringMatching(/^book-2\.epubQueued/),
      expect.stringMatching(/^book-1\.epubAdded/),
    ]);
  });

  it('shows the percentage of the file being sent', async () => {
    sendTwoFiles(40);

    const popover = await openPopover();

    expect(popover.textContent).toContain('Uploading 40%');
    const bar = popover.querySelector('[data-slot="progress"]');
    expect(bar?.getAttribute('aria-valuenow')).toBe('40');
  });

  it('links an added upload to its work', async () => {
    await listUploads([
      upload(1, 'added', { workId }),
      upload(2, 'duplicate', { workId }),
      upload(3, 'running'),
    ]);

    const popover = await openPopover();

    const links = Array.from(popover.querySelectorAll('a')).map((link) => [
      link.textContent?.trim(),
      link.getAttribute('href'),
    ]);
    expect(links).toEqual([
      ['Added', `/works/${workId}`],
      ['Duplicate', `/works/${workId}`],
    ]);
    expect(popover.textContent).toContain('Ingesting');
  });

  it('words a failure by its kind and never shows a raw error', async () => {
    await listUploads([
      upload(1, 'failed', { failure: 'unsupportedFormat' }),
      upload(2, 'failed', { failure: 'unreadable' }),
      upload(3, 'failed', { failure: 'internal' }),
      upload(4, 'failed', { failure: null }),
    ]);

    const popover = await openPopover();

    const [format, unreadable, internal, unknown] = rowsOf(popover);
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
    const popover = await openPopover();

    button(popover, 'Retry').click();
    const retry = await vi.waitFor(() =>
      httpTesting.expectOne(`/api/activity/uploads/${failed.jobId}/retry`),
    );
    expect(retry.request.method).toBe('POST');
    retry.flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();

    button(popover, 'Remove').click();
    const remove = await vi.waitFor(() =>
      httpTesting.expectOne(`/api/activity/uploads/${failed.jobId}`),
    );
    expect(remove.request.method).toBe('DELETE');
    remove.flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();

    // The row changes only when the hub says so.
    expect(rowsOf(popover)).toHaveLength(1);
    hub.uploadRemoved.next(failed.jobId);
    await fixture.whenStable();
    expect(rowsOf(popover)).toEqual([]);
  });

  it('says so on the row when a retry fails', async () => {
    const failed = upload(1, 'failed', { failure: 'internal' });
    await listUploads([failed]);
    const popover = await openPopover();

    button(popover, 'Retry').click();
    (
      await vi.waitFor(() => httpTesting.expectOne(`/api/activity/uploads/${failed.jobId}/retry`))
    ).flush({ title: 'Server Error', status: 500 } satisfies ProblemDetails, {
      status: 500,
      statusText: 'Server Error',
    });
    await fixture.whenStable();

    await vi.waitFor(() => expect(rowsOf(popover)[0]).toContain(somethingWentWrong));
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
    const popover = await openPopover();

    expect(rowsOf(popover)[0]).toContain('Failed');
    expect(rowsOf(popover)[0]).toContain('Too big for this library.');

    button(popover, 'Retry').click();
    expect(queue.rows()[0].status).toBe('uploading');
    httpTesting
      .expectOne('/api/uploads')
      .flush(
        { title: 'Payload Too Large', status: 413, detail: 'Too big for this library.' },
        { status: 413, statusText: 'Payload Too Large' },
      );
    await fixture.whenStable();

    button(popover, 'Remove').click();
    expect(queue.rows()).toEqual([]);
  });

  it('counts the uploads in flight on its badge', async () => {
    expect(badge()).toBeNull();

    await listUploads([upload(1, 'queued'), upload(2, 'running'), upload(3, 'added')]);
    sendTwoFiles(10);
    await fixture.whenStable();

    expect(badge()?.textContent?.trim()).toBe('4');
    expect(badge()?.getAttribute('data-variant')).toBe('default');
  });

  it('turns the badge to the failed count when any upload has failed', async () => {
    await listUploads([
      upload(1, 'queued'),
      upload(2, 'failed', { failure: 'internal' }),
      upload(3, 'failed', { failure: 'unreadable' }),
    ]);
    queue.add([epub('dune.epub')], null);
    httpTesting
      .expectOne('/api/uploads')
      .flush({ title: 'Bad Request', status: 400 }, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();

    expect(badge()?.textContent?.trim()).toBe('3');
    expect(badge()?.getAttribute('data-variant')).toBe('destructive');
  });

  it('shows no badge with nothing in flight and nothing failed', async () => {
    await listUploads([upload(1, 'added', { workId })]);

    expect(badge()).toBeNull();
  });

  it('says there is nothing yet when the list is empty', async () => {
    const popover = await openPopover();

    expect(popover.textContent).toContain('Activity');
    expect(popover.textContent).toContain('No uploads yet');
  });
});
