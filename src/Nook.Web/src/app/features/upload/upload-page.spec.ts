import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { IngestResult } from '../../api/jobs-api';
import { components } from '../../api/schema';
import { buttonLabelled } from '../../testing/button-labelled';
import { text } from '../../testing/element-text';
import { UploadPage } from './upload-page';
import { UploadQueue } from './upload-queue';

type JobDetailResponse = components['schemas']['JobDetailResponse'];
type SectionResponse = components['schemas']['SectionResponse'];
type ProblemDetails = components['schemas']['ProblemDetails'];
type UploadResponse = components['schemas']['UploadResponse'];

describe('UploadPage', () => {
  const books: SectionResponse = { id: '0198a1b2-0000-7000-8000-00000000000a', name: 'Books' };
  const comics: SectionResponse = { id: '0198a1b2-0000-7000-8000-00000000000b', name: 'Comics' };

  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'upload', component: UploadPage }]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => {
    // Cancels the upload a test left open and drops the files still waiting, so nothing is sent after the test.
    TestBed.inject(UploadQueue).reset();
    httpTesting.verify();
  });

  function epub(name: string): File {
    return new File(['epub bytes'], name, { type: 'application/epub+zip' });
  }

  /** Opens the page at `url` and answers its request for sections with `sections`. */
  async function openPage(url: string, sections: SectionResponse[]): Promise<HTMLElement> {
    const navigating = harness.navigateByUrl(url);
    const request = await vi.waitFor(() => httpTesting.expectOne('/api/sections'));
    request.flush(sections);
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await vi.waitFor(() => expect(page.querySelector('input, p')).not.toBeNull());
    return page;
  }

  function fileInput(page: HTMLElement): HTMLInputElement {
    const input = page.querySelector<HTMLInputElement>('input[type="file"]');
    if (!input) {
      throw new Error('No file input');
    }
    return input;
  }

  function dropZone(page: HTMLElement): HTMLElement {
    const zone = page.querySelector<HTMLElement>('[data-drop-zone]');
    if (!zone) {
      throw new Error('No drop zone');
    }
    return zone;
  }

  function sectionPicker(page: HTMLElement): HTMLSelectElement | null {
    return page.querySelector('select');
  }

  /** Picks `files` with the file input, as the browser does once the user has chosen them. */
  function pick(page: HTMLElement, files: File[]): void {
    const input = fileInput(page);
    Object.defineProperty(input, 'files', { value: files, configurable: true });
    input.dispatchEvent(new Event('change'));
  }

  /** Drops `files` on the drop zone. */
  function drop(page: HTMLElement, files: File[]): Event {
    const event = new Event('drop', { bubbles: true, cancelable: true });
    Object.defineProperty(event, 'dataTransfer', { value: { files } });
    dropZone(page).dispatchEvent(event);
    return event;
  }

  /** The one open upload, after checking it sends `file` to `sectionId`, or to the Default area when it is null. */
  function expectUpload(file: File, sectionId: string | null): TestRequest {
    const request = httpTesting.expectOne('/api/uploads');
    expect(request.request.method).toBe('POST');
    const form = request.request.body as FormData;
    expect(form.has('sectionId')).toBe(sectionId !== null);
    expect(form.get('sectionId')).toBe(sectionId);
    expect(form.get('file')).toBe(file);
    return request;
  }

  /** The name of each option the section picker offers, in order. */
  function offered(page: HTMLElement): string[] {
    return Array.from(sectionPicker(page)!.options, (option) => option.text.trim());
  }

  /** The name of the option the section picker shows as chosen. */
  function chosen(page: HTMLElement): string {
    return sectionPicker(page)!.selectedOptions[0]?.text.trim() ?? '';
  }

  /** Chooses the option named `name` in the section picker. */
  function choose(page: HTMLElement, name: string): void {
    const picker = sectionPicker(page)!;
    const option = Array.from(picker.options).find((candidate) => candidate.text.trim() === name);
    if (!option) {
      throw new Error(`The picker offers no ${name}`);
    }
    picker.value = option.value;
    picker.dispatchEvent(new Event('change'));
  }

  /** Whether the page lets files be added: the input and the drop zone both take them. */
  function takesFiles(page: HTMLElement): boolean {
    return !fileInput(page).disabled && dropZone(page).getAttribute('aria-disabled') === null;
  }

  it('the picker takes several files and offers epub files', async () => {
    const page = await openPage('/upload', []);

    const input = fileInput(page);
    expect(input.multiple).toBe(true);
    expect(input.accept).toBe('.epub,application/epub+zip');
    expect(text(page.querySelector(`label[for="${input.id}"]`))).not.toBe('');
    expect(dropZone(page).contains(input)).toBe(true);
  });

  it('with no sections, picked files go to the default area and no picker shows', async () => {
    const page = await openPage('/upload', []);
    expect(sectionPicker(page)).toBeNull();
    expect(takesFiles(page)).toBe(true);

    const dune = epub('dune.epub');
    const emma = epub('emma.epub');
    pick(page, [dune, emma]);

    expectUpload(dune, null);
    expect(
      TestBed.inject(UploadQueue)
        .rows()
        .map((row) => [row.name, row.sectionId]),
    ).toEqual([
      ['dune.epub', null],
      ['emma.epub', null],
    ]);
  });

  it('dropped files are sent', async () => {
    const page = await openPage('/upload', []);

    const dune = epub('dune.epub');
    const event = drop(page, [dune]);

    expect(event.defaultPrevented).toBe(true);
    expectUpload(dune, null);
  });

  it('a file that is not an epub is still sent', async () => {
    const page = await openPage('/upload', []);

    const notes = new File(['some text'], 'notes.txt', { type: 'text/plain' });
    pick(page, [notes]);
    expectUpload(notes, null);

    const queue = TestBed.inject(UploadQueue);
    queue.reset();
    const cover = new File(['png bytes'], 'cover.png', { type: 'image/png' });
    drop(page, [cover]);
    expectUpload(cover, null);
  });

  it('with sections the picker offers Default first and then each section', async () => {
    const page = await openPage('/upload', [books, comics]);

    const picker = sectionPicker(page)!;
    expect(text(page.querySelector(`label[for="${picker.id}"]`))).toBe('Section');
    expect(offered(page)).toEqual(['Default', 'Books', 'Comics']);
    expect(page.textContent).not.toContain('Choose a section');
  });

  it('the section named in the URL is preselected', async () => {
    const page = await openPage(`/upload?section=${comics.id}`, [books, comics]);

    expect(chosen(page)).toBe('Comics');
    expect(takesFiles(page)).toBe(true);

    const dune = epub('dune.epub');
    pick(page, [dune]);
    expectUpload(dune, comics.id);
  });

  it('Default is preselected when the URL names no section', async () => {
    const page = await openPage('/upload', [books]);

    expect(offered(page)).toEqual(['Default', 'Books']);
    expect(chosen(page)).toBe('Default');
    expect(takesFiles(page)).toBe(true);
  });

  it('Default is preselected when the URL says section=default', async () => {
    const page = await openPage('/upload?section=default', [books, comics]);

    expect(chosen(page)).toBe('Default');
    expect(takesFiles(page)).toBe(true);
  });

  it('a section in the URL the user cannot see counts as Default', async () => {
    const page = await openPage('/upload?section=0198a1b2-0000-7000-8000-0000000000ff', [
      books,
      comics,
    ]);

    expect(chosen(page)).toBe('Default');
    expect(takesFiles(page)).toBe(true);

    const dune = epub('dune.epub');
    pick(page, [dune]);
    expectUpload(dune, null);
  });

  it('files added after choosing a section go to it', async () => {
    const page = await openPage('/upload', [books, comics]);

    choose(page, 'Books');
    const dune = epub('dune.epub');
    pick(page, [dune]);

    expectUpload(dune, books.id);
  });

  it('files added with Default chosen carry no section', async () => {
    const page = await openPage(`/upload?section=${comics.id}`, [books, comics]);

    choose(page, 'Default');
    const dune = epub('dune.epub');
    drop(page, [dune]);

    expectUpload(dune, null);
    expect(TestBed.inject(UploadQueue).rows()[0].sectionId).toBeNull();
  });

  it('says something went wrong when the sections cannot be loaded', async () => {
    const navigating = harness.navigateByUrl('/upload');
    (await vi.waitFor(() => httpTesting.expectOne('/api/sections'))).flush(null, {
      status: 500,
      statusText: 'Internal Server Error',
    });
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;

    await vi.waitFor(() =>
      expect(text(page.querySelector('[role="alert"]'))).toBe('Something went wrong. Try again.'),
    );
    expect(page.querySelector('input, select, [data-drop-zone]')).toBeNull();
  });

  describe('rows', () => {
    const duneJobId = '0198a1b2-0000-7000-8000-0000000000d1';
    const emmaJobId = '0198a1b2-0000-7000-8000-0000000000d2';
    const frankJobId = '0198a1b2-0000-7000-8000-0000000000d3';
    const ginaJobId = '0198a1b2-0000-7000-8000-0000000000d4';
    const duneWorkId = '0198a1b2-0000-7000-8000-000000000001';
    const emmaWorkId = '0198a1b2-0000-7000-8000-000000000002';
    const itemId = '0198a1b2-0000-7000-8000-0000000000e1';

    let page: HTMLElement;

    // The page is opened on real timers, since `vi.waitFor` would fire the queue's polls under fake ones. From the
    // first file on, time moves only when a test moves it.
    beforeEach(async () => {
      page = await openPage('/upload', []);
      vi.useFakeTimers();
    });

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

    function succeeded(id: string, outcome: IngestResult['outcome'], workId: string) {
      const result: IngestResult = { outcome, itemId, workId };
      return job(id, 'succeeded', { result });
    }

    /** Lets what is under way land and shows it. */
    async function settle(): Promise<void> {
      await vi.advanceTimersByTimeAsync(0);
      harness.detectChanges();
    }

    /** Answers the open upload of `file` with `jobId`. */
    async function accept(file: File, jobId: string): Promise<void> {
      const response: UploadResponse = { jobId };
      expectUpload(file, null).flush(response, { status: 202, statusText: 'Accepted' });
      await settle();
    }

    /** Answers the open upload of `file` with a refusal. */
    async function refuse(file: File, problem: ProblemDetails): Promise<void> {
      expectUpload(file, null).flush(problem, {
        status: problem.status ?? 500,
        statusText: 'Refused',
      });
      await settle();
    }

    /** Lets a second pass, then answers the poll of each job named with its job. */
    async function answerPolls(answers: Record<string, JobDetailResponse>): Promise<void> {
      await vi.advanceTimersByTimeAsync(1000);
      for (const [jobId, body] of Object.entries(answers)) {
        const request = httpTesting.expectOne(`/api/jobs/${jobId}`);
        expect(request.request.method).toBe('GET');
        request.flush(body);
      }
      await settle();
    }

    async function pickFiles(files: File[]): Promise<void> {
      pick(page, files);
      await settle();
    }

    function rows(): HTMLElement[] {
      return Array.from(page.querySelectorAll<HTMLElement>('ul[aria-label="Files"] > li'));
    }

    function row(name: string): HTMLElement {
      const found = rows().find((candidate) => fileName(candidate) === name);
      if (!found) {
        throw new Error(`No row for ${name}`);
      }
      return found;
    }

    function fileName(element: HTMLElement): string {
      return text(element.querySelector('[data-file-name]'));
    }

    /** Each row's file name and status, top to bottom. */
    function shown(): string[][] {
      return rows().map((candidate) => [
        fileName(candidate),
        text(candidate.querySelector('[data-slot="badge"]')),
      ]);
    }

    function buttonsOf(element: HTMLElement): string[] {
      return Array.from(element.querySelectorAll('button')).map((button) => text(button));
    }
    function clearFinished(): HTMLButtonElement | undefined {
      return Array.from(page.querySelectorAll('button')).find(
        (candidate) => text(candidate) === 'Clear finished',
      );
    }

    async function failedJob(file: File, jobId: string): Promise<void> {
      await pickFiles([file]);
      await accept(file, jobId);
      await answerPolls({ [jobId]: job(jobId, 'failed', { error: 'The file is not an EPUB.' }) });
    }

    it('each file picked gets a row showing its name, in the order added', async () => {
      const dune = epub('dune.epub');
      const emma = epub('emma.epub');
      const frank = epub('frankenstein.epub');

      await pickFiles([dune, emma]);
      drop(page, [frank]);
      await settle();

      expect(rows().map(fileName)).toEqual(['dune.epub', 'emma.epub', 'frankenstein.epub']);
      expectUpload(dune, null);
    });

    it('a file shows waiting, uploading, ingesting and then added as it goes through', async () => {
      const dune = epub('dune.epub');
      const emma = epub('emma.epub');

      await pickFiles([dune, emma]);
      expect(shown()).toEqual([
        ['dune.epub', 'Uploading'],
        ['emma.epub', 'Waiting'],
      ]);

      await accept(dune, duneJobId);
      expect(shown()).toEqual([
        ['dune.epub', 'Ingesting'],
        ['emma.epub', 'Uploading'],
      ]);
      expectUpload(emma, null);

      await answerPolls({ [duneJobId]: job(duneJobId, 'running') });
      expect(shown()[0]).toEqual(['dune.epub', 'Ingesting']);

      await answerPolls({ [duneJobId]: succeeded(duneJobId, 'created', duneWorkId) });
      expect(shown()[0]).toEqual(['dune.epub', 'Added']);
    });

    it('a duplicate shows as a duplicate', async () => {
      const dune = epub('dune.epub');
      await pickFiles([dune]);
      await accept(dune, duneJobId);

      await answerPolls({ [duneJobId]: succeeded(duneJobId, 'duplicate', duneWorkId) });

      expect(shown()).toEqual([['dune.epub', 'Duplicate']]);
    });

    it('a failed job shows its error', async () => {
      await failedJob(epub('dune.epub'), duneJobId);

      expect(shown()).toEqual([['dune.epub', 'Failed']]);
      expect(text(row('dune.epub'))).toContain('The file is not an EPUB.');
    });

    it("a refused upload shows the server's message", async () => {
      const dune = epub('dune.epub');
      await pickFiles([dune]);

      await refuse(dune, { status: 413, detail: 'The file is larger than 200 MB.' });

      expect(shown()).toEqual([['dune.epub', 'Failed']]);
      expect(text(row('dune.epub'))).toContain('The file is larger than 200 MB.');
    });

    it('an added row and a duplicate row link to their work', async () => {
      const dune = epub('dune.epub');
      const emma = epub('emma.epub');
      const frank = epub('frankenstein.epub');
      await pickFiles([dune, emma, frank]);
      await accept(dune, duneJobId);
      await accept(emma, emmaJobId);
      expectUpload(frank, null);

      await answerPolls({
        [duneJobId]: succeeded(duneJobId, 'created', duneWorkId),
        [emmaJobId]: succeeded(emmaJobId, 'duplicate', emmaWorkId),
      });

      const link = (name: string) =>
        row(name).querySelector<HTMLAnchorElement>('a[data-file-name]')?.getAttribute('href');
      expect(link('dune.epub')).toBe(`/works/${duneWorkId}`);
      expect(link('emma.epub')).toBe(`/works/${emmaWorkId}`);
      expect(row('frankenstein.epub').querySelector('a')).toBeNull();
    });

    it('a failed row offers Retry and Remove and no other row does', async () => {
      const dune = epub('dune.epub');
      const emma = epub('emma.epub');
      const frank = epub('frankenstein.epub');
      const gina = epub('gina.epub');
      const hugo = epub('hugo.epub');
      await pickFiles([dune, emma, frank, gina, hugo]);
      await refuse(dune, { status: 413, detail: 'The file is larger than 200 MB.' });
      await accept(emma, emmaJobId);
      await accept(frank, frankJobId);
      await accept(gina, ginaJobId);
      expectUpload(hugo, null);

      await answerPolls({
        [emmaJobId]: job(emmaJobId, 'failed', { error: 'Broken.' }),
        [frankJobId]: succeeded(frankJobId, 'created', duneWorkId),
        [ginaJobId]: job(ginaJobId, 'running'),
      });

      expect(shown()).toEqual([
        ['dune.epub', 'Failed'],
        ['emma.epub', 'Failed'],
        ['frankenstein.epub', 'Added'],
        ['gina.epub', 'Ingesting'],
        ['hugo.epub', 'Uploading'],
      ]);
      expect(buttonsOf(row('dune.epub'))).toEqual(['Retry', 'Remove']);
      expect(buttonsOf(row('emma.epub'))).toEqual(['Retry', 'Remove']);
      expect(buttonsOf(row('frankenstein.epub'))).toEqual([]);
      expect(buttonsOf(row('gina.epub'))).toEqual([]);
      expect(buttonsOf(row('hugo.epub'))).toEqual([]);
    });

    it('Retry on a failed job reruns it and shows ingesting', async () => {
      await failedJob(epub('dune.epub'), duneJobId);

      buttonLabelled(row('dune.epub'), 'Retry').click();
      const request = httpTesting.expectOne(`/api/jobs/${duneJobId}/retry`);
      expect(request.request.method).toBe('POST');
      request.flush(null, { status: 204, statusText: 'No Content' });
      await settle();

      expect(shown()).toEqual([['dune.epub', 'Ingesting']]);
      expect(text(row('dune.epub'))).not.toContain('The file is not an EPUB.');
    });

    it('Retry on a failed upload sends the file again', async () => {
      const dune = epub('dune.epub');
      await pickFiles([dune]);
      await refuse(dune, { status: 502, title: 'Bad Gateway' });

      buttonLabelled(row('dune.epub'), 'Retry').click();
      await settle();

      expectUpload(dune, null);
      expect(shown()).toEqual([['dune.epub', 'Uploading']]);
    });

    it('Remove on a failed job deletes the job and the row goes', async () => {
      await failedJob(epub('dune.epub'), duneJobId);

      buttonLabelled(row('dune.epub'), 'Remove').click();
      const request = httpTesting.expectOne(`/api/jobs/${duneJobId}`);
      expect(request.request.method).toBe('DELETE');
      request.flush(null, { status: 204, statusText: 'No Content' });
      await settle();

      expect(rows()).toEqual([]);
    });

    it('Remove on a failed upload takes the row away', async () => {
      const dune = epub('dune.epub');
      await pickFiles([dune]);
      await refuse(dune, { status: 502, title: 'Bad Gateway' });

      buttonLabelled(row('dune.epub'), 'Remove').click();
      await settle();

      expect(rows()).toEqual([]);
    });

    it('Clear finished takes away added and duplicate rows only', async () => {
      const dune = epub('dune.epub');
      const emma = epub('emma.epub');
      const frank = epub('frankenstein.epub');
      const gina = epub('gina.epub');
      const hugo = epub('hugo.epub');
      await pickFiles([dune, emma, frank, gina, hugo]);
      expect(clearFinished()).toBeUndefined();
      await accept(dune, duneJobId);
      await accept(emma, emmaJobId);
      await accept(frank, frankJobId);
      expectUpload(gina, null);

      await answerPolls({
        [duneJobId]: succeeded(duneJobId, 'created', duneWorkId),
        [emmaJobId]: succeeded(emmaJobId, 'duplicate', emmaWorkId),
        [frankJobId]: job(frankJobId, 'failed', { error: 'Broken.' }),
      });

      clearFinished()!.click();
      await settle();

      expect(shown()).toEqual([
        ['frankenstein.epub', 'Failed'],
        ['gina.epub', 'Uploading'],
        ['hugo.epub', 'Waiting'],
      ]);
      expect(clearFinished()).toBeUndefined();
    });
  });
});
