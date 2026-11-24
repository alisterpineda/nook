import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { components } from '../../api/schema';
import { text } from '../../testing/element-text';
import { AuditLogPage } from './audit-log-page';

type AuditEntryResponse = components['schemas']['AuditEntryResponse'];
type PageOfAuditEntryResponse = components['schemas']['PageOfAuditEntryResponse'];
type AuditFiltersResponse = components['schemas']['AuditFiltersResponse'];

@Component({ template: 'A work' })
class WorkStub {}

describe('AuditLogPage', () => {
  const adaId = '0198a1b2-0000-7000-8000-0000000000a1';
  const graceId = '0198a1b2-0000-7000-8000-0000000000a2';
  const workId = '0198a1b2-0000-7000-8000-0000000000b1';

  function anEntry(
    number: number,
    overrides: Partial<AuditEntryResponse> = {},
  ): AuditEntryResponse {
    return {
      id: `0198a1b2-0000-7000-8000-00000000000${number}`,
      at: '2026-11-14T12:00:00Z',
      actorId: adaId,
      actorName: 'Ada',
      actorEmail: 'ada@example.com',
      action: 'job.retried',
      subjectType: 'job',
      subjectId: '0198a1b2-0000-7000-8000-0000000000c1',
      subjectName: 'ingest',
      details: {},
      ...overrides,
    };
  }

  const retried = anEntry(1);
  const added = anEntry(2, {
    actorId: graceId,
    actorName: 'Grace',
    actorEmail: 'grace@example.com',
    action: 'upload.added',
    subjectType: 'work',
    subjectId: workId,
    subjectName: 'Dune',
  });
  const unknown = anEntry(3, { action: 'library.merged', subjectType: 'library', subjectId: null });

  const filters: AuditFiltersResponse = {
    actors: [
      { id: adaId, name: 'Ada', email: 'ada@example.com' },
      { id: graceId, name: 'Grace', email: 'grace@example.com' },
    ],
    actions: ['job.retried', 'upload.added'],
  };

  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let router: Router;

  // The filters' selects bring a chosen item into view, which the test DOM has no way to do.
  beforeAll(() => {
    Element.prototype.scrollIntoView ??= () => undefined;
  });

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'audit-log', component: AuditLogPage },
          { path: 'works/:id', component: WorkStub },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => httpTesting.verify());

  function pageOf(items: AuditEntryResponse[], total = items.length): PageOfAuditEntryResponse {
    return { items, total, offset: 0, limit: 50 };
  }

  function logRequest(): Promise<TestRequest> {
    return vi.waitFor(() => httpTesting.expectOne((request) => request.url === '/api/audit-log'));
  }

  async function answerFilters(answer: AuditFiltersResponse = filters): Promise<void> {
    (await vi.waitFor(() => httpTesting.expectOne('/api/audit-log/filters'))).flush(answer);
  }

  /** Waits for the page to show what the API answered: the table, the empty state or an error. */
  async function shown(page: HTMLElement): Promise<void> {
    await vi.waitFor(() =>
      expect(page.querySelector('table, [data-slot="empty"], [role="alert"]')).not.toBeNull(),
    );
  }

  /** Opens the page at `url` and answers its requests, the log with `entries` out of `total` in all. */
  async function openLog(
    url: string,
    entries: AuditEntryResponse[] = [retried, added],
    total = entries.length,
  ): Promise<{ page: HTMLElement; request: TestRequest }> {
    const navigating = harness.navigateByUrl(url);
    const request = await logRequest();
    request.flush(pageOf(entries, total));
    await answerFilters();
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await shown(page);
    return { page, request };
  }

  function rows(page: HTMLElement): HTMLTableRowElement[] {
    return Array.from(page.querySelectorAll<HTMLTableRowElement>('tbody tr'));
  }

  function rowTexts(page: HTMLElement): string[][] {
    return rows(page).map((row) => Array.from(row.querySelectorAll('td'), (cell) => text(cell)));
  }

  function buttonNamed(scope: HTMLElement, name: string): HTMLButtonElement {
    const button = Array.from(scope.querySelectorAll('button')).find(
      (candidate) => text(candidate) === name,
    );
    if (!button) {
      throw new Error(`No button named ${name}`);
    }
    return button;
  }

  /** The choices of an open select's list, which is shown over the page. */
  function listed(): HTMLElement[] {
    harness.detectChanges();
    return Array.from(document.querySelectorAll<HTMLElement>('[role="option"]'));
  }

  /** The button that opens a filter's list. */
  function filter(page: HTMLElement, name: string): HTMLElement {
    const label = Array.from(page.querySelectorAll('label')).find((one) => text(one) === name);
    const trigger = label ? page.querySelector<HTMLElement>(`#${label.htmlFor}`) : null;
    if (!trigger) {
      throw new Error(`No filter named ${name}`);
    }
    return trigger;
  }

  async function choose(page: HTMLElement, name: string, choice: string): Promise<void> {
    filter(page, name).click();
    await vi.waitFor(() => expect(listed().map((option) => text(option))).toContain(choice));
    listed()
      .find((option) => text(option) === choice)!
      .click();
    await vi.waitFor(() => expect(listed()).toEqual([]));
  }

  it("shows each entry's time, person, action and subject", async () => {
    const { page, request } = await openLog('/audit-log');

    expect(request.request.params.get('Offset')).toBe('0');
    expect(request.request.params.get('Limit')).toBe('50');
    expect(request.request.params.has('ActorId')).toBe(false);
    expect(request.request.params.has('Action')).toBe(false);
    expect(text(page.querySelector('h1'))).toBe('Audit log');
    const [first, second] = rowTexts(page);
    expect(first[0]).not.toBe('');
    expect(first.slice(1)).toEqual(['Adaada@example.com', 'Retried a job', 'ingest']);
    expect(second.slice(1)).toEqual(['Gracegrace@example.com', 'Upload added a book', 'Dune']);
  });

  it('shows an action nothing words by its own name', async () => {
    const { page } = await openLog('/audit-log', [unknown]);

    expect(rowTexts(page)[0][2]).toBe('library.merged');
  });

  it('words an edit and a delete of a book', async () => {
    const edited = anEntry(4, {
      action: 'work.edited',
      subjectType: 'work',
      subjectId: workId,
      subjectName: 'Dune',
    });
    const deleted = anEntry(5, {
      action: 'work.deleted',
      subjectType: 'work',
      subjectId: workId,
      subjectName: 'Dune',
    });
    const { page } = await openLog('/audit-log', [edited, deleted]);

    expect(rowTexts(page).map((row) => row[2])).toEqual(['Edited a book', 'Deleted a book']);
    expect(text(page)).not.toContain('work.edited');
    expect(text(page)).not.toContain('work.deleted');
  });

  it('links an entry about a work to that work', async () => {
    const { page } = await openLog('/audit-log', [retried, added]);

    const [jobRow, workRow] = rows(page);
    expect(jobRow.querySelector('a')).toBeNull();
    const link = workRow.querySelector('a');
    expect(text(link)).toBe('Dune');
    expect(link?.getAttribute('href')).toBe(`/works/${workId}`);
  });

  it('shows the subject of an entry with no id as plain text', async () => {
    const { page } = await openLog('/audit-log', [
      anEntry(4, { subjectType: 'work', subjectId: null }),
    ]);

    expect(page.querySelector('tbody a')).toBeNull();
  });

  it('offers every person and every action to filter by', async () => {
    const { page } = await openLog('/audit-log');

    filter(page, 'Person').click();
    await vi.waitFor(() =>
      expect(listed().map((option) => text(option))).toEqual(['Everyone', 'Ada', 'Grace']),
    );
    listed()[0].click();
    await vi.waitFor(() => expect(listed()).toEqual([]));

    filter(page, 'Action').click();
    await vi.waitFor(() =>
      expect(listed().map((option) => text(option))).toEqual([
        'Every action',
        'Retried a job',
        'Upload added a book',
      ]),
    );
  });

  it('narrows the list to the person chosen', async () => {
    const { page } = await openLog('/audit-log?page=2', [retried], 120);

    await choose(page, 'Person', 'Grace');
    const request = await logRequest();
    request.flush(pageOf([added]));
    await vi.waitFor(() => expect(rows(page).length).toBe(1));

    expect(router.url).toBe(`/audit-log?person=${graceId}`);
    expect(request.request.params.get('ActorId')).toBe(graceId);
    expect(request.request.params.get('Offset')).toBe('0');
    expect(text(filter(page, 'Person'))).toBe('Grace');
  });

  it('narrows the list to the action chosen', async () => {
    const { page } = await openLog('/audit-log?page=2', [retried], 120);

    await choose(page, 'Action', 'Upload added a book');
    const request = await logRequest();
    request.flush(pageOf([added]));
    await vi.waitFor(() => expect(rows(page).length).toBe(1));

    expect(router.url).toBe('/audit-log?action=upload.added');
    expect(request.request.params.get('Action')).toBe('upload.added');
    expect(request.request.params.get('Offset')).toBe('0');
  });

  it('opens narrowed to the person and action the URL names', async () => {
    const { page, request } = await openLog(`/audit-log?person=${graceId}&action=upload.added`, [
      added,
    ]);

    expect(request.request.params.get('ActorId')).toBe(graceId);
    expect(request.request.params.get('Action')).toBe('upload.added');
    expect(text(filter(page, 'Person'))).toBe('Grace');
    expect(text(filter(page, 'Action'))).toBe('Upload added a book');
  });

  it('takes a filter out of the URL when everyone or every action is chosen', async () => {
    const { page } = await openLog(`/audit-log?person=${graceId}&action=upload.added`, [added]);

    await choose(page, 'Person', 'Everyone');
    (await logRequest()).flush(pageOf([added]));
    await vi.waitFor(() => expect(router.url).toBe('/audit-log?action=upload.added'));

    await choose(page, 'Action', 'Every action');
    const request = await logRequest();
    request.flush(pageOf([retried, added]));
    await vi.waitFor(() => expect(rows(page).length).toBe(2));

    expect(router.url).toBe('/audit-log');
    expect(request.request.params.has('ActorId')).toBe(false);
    expect(request.request.params.has('Action')).toBe(false);
  });

  it('pages through the list', async () => {
    const { page } = await openLog('/audit-log', [retried, added], 120);
    expect(page.textContent).toContain('Page 1 of 3');
    expect(buttonNamed(page, 'Previous').disabled).toBe(true);

    buttonNamed(page, 'Next').click();
    const request = await logRequest();
    request.flush(pageOf([unknown], 120));
    await vi.waitFor(() => expect(rows(page).length).toBe(1));

    expect(router.url).toBe('/audit-log?page=2');
    expect(request.request.params.get('Offset')).toBe('50');
    expect(page.textContent).toContain('Page 2 of 3');
  });

  it('keeps the filters when paging', async () => {
    const { page } = await openLog('/audit-log?action=job.retried', [retried], 120);

    buttonNamed(page, 'Next').click();
    const request = await logRequest();
    request.flush(pageOf([retried], 120));
    await vi.waitFor(() => expect(router.url).toBe('/audit-log?action=job.retried&page=2'));

    expect(request.request.params.get('Action')).toBe('job.retried');
  });

  it('says nothing is logged when the log is empty', async () => {
    const { page } = await openLog('/audit-log', []);

    expect(page.querySelector('table')).toBeNull();
    expect(page.textContent).toContain('Nothing logged yet');
  });

  it('says no entries match when a filter leaves none', async () => {
    const { page } = await openLog(`/audit-log?person=${adaId}`, []);

    expect(page.textContent).toContain('No entries match');
  });

  it('still shows the log when the filters fail to load', async () => {
    const navigating = harness.navigateByUrl('/audit-log');
    (await logRequest()).flush(pageOf([retried]));
    (await vi.waitFor(() => httpTesting.expectOne('/api/audit-log/filters'))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await shown(page);

    expect(rows(page).length).toBe(1);
  });

  it('offers to try again when the list fails to load', async () => {
    const navigating = harness.navigateByUrl('/audit-log');
    (await logRequest()).flush(null, { status: 500, statusText: 'Server Error' });
    await answerFilters();
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await shown(page);
    expect(page.querySelector('table')).toBeNull();

    buttonNamed(page, 'Retry').click();
    (await logRequest()).flush(pageOf([retried]));

    await vi.waitFor(() => expect(rows(page).length).toBe(1));
  });
});
