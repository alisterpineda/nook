import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { components } from '../../api/schema';
import { text } from '../../testing/element-text';
import { FakeActivityHub, provideFakeActivityHub } from '../../testing/fake-activity-hub';
import { JobsPage } from './jobs-page';

type JobSummaryResponse = components['schemas']['JobSummaryResponse'];
type PageOfJobSummaryResponse = components['schemas']['PageOfJobSummaryResponse'];

describe('JobsPage', () => {
  function aJob(number: number, overrides: Partial<JobSummaryResponse> = {}): JobSummaryResponse {
    return {
      id: `0198a1b2-0000-7000-8000-00000000000${number}`,
      type: 'ingest',
      status: 'pending',
      error: null,
      parentJobId: null,
      notBefore: null,
      attemptsMade: 0,
      attemptsAllowed: 3,
      reclaimsMade: 0,
      createdAt: '2026-11-14T12:00:00Z',
      updatedAt: '2026-11-14T12:00:00Z',
      submittedBy: null,
      ...overrides,
    };
  }

  const waiting = aJob(1);
  const running = aJob(2, { status: 'running', attemptsMade: 1, submittedBy: 'Ada' });
  const failed = aJob(3, {
    status: 'failed',
    attemptsMade: 3,
    error: 'The file ended too soon.',
    submittedBy: 'Grace',
  });

  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let router: Router;
  let hub: FakeActivityHub;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'jobs', component: JobsPage }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideFakeActivityHub(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    hub = TestBed.inject(FakeActivityHub);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => httpTesting.verify());

  function pageOf(items: JobSummaryResponse[], total = items.length): PageOfJobSummaryResponse {
    return { items, total, offset: 0, limit: 50 };
  }

  function jobsRequest(): Promise<TestRequest> {
    return vi.waitFor(() => httpTesting.expectOne((request) => request.url === '/api/jobs'));
  }

  /** Waits for the page to show what the API answered: the table, the empty state or an error. */
  async function shown(page: HTMLElement): Promise<void> {
    await vi.waitFor(() =>
      expect(page.querySelector('table, [data-slot="empty"], [role="alert"]')).not.toBeNull(),
    );
  }

  /** Opens the page at `url` and answers its request for jobs with `jobs`, out of `total` in all. */
  async function openList(
    url: string,
    jobs: JobSummaryResponse[] = [waiting, running, failed],
    total = jobs.length,
  ): Promise<{ page: HTMLElement; request: TestRequest }> {
    const navigating = harness.navigateByUrl(url);
    const request = await jobsRequest();
    request.flush(pageOf(jobs, total));
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await shown(page);
    return { page, request };
  }

  /** The row of each job, without the rows under them that carry an error or a problem. */
  function rows(page: HTMLElement): HTMLTableRowElement[] {
    return Array.from(page.querySelectorAll<HTMLTableRowElement>('tbody tr')).filter(
      (row) => row.querySelector('td[colspan]') === null,
    );
  }

  /** What the row under `row` says, when it has one. */
  function noteUnder(row: HTMLElement): string {
    const next = row.nextElementSibling;
    return next?.querySelector('td[colspan]') ? text(next) : '';
  }

  /** What each row says, cell by cell, leaving out the buttons. */
  function rowTexts(page: HTMLElement): string[][] {
    return rows(page).map((row) =>
      Array.from(row.querySelectorAll('td'))
        .slice(0, 5)
        .map((cell) => text(cell)),
    );
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

  function buttonsOf(row: HTMLElement): string[] {
    return Array.from(row.querySelectorAll('button')).map(text);
  }

  it('opens on the jobs that are not finished', async () => {
    const { page, request } = await openList('/jobs');

    expect(request.request.params.getAll('Status')).toEqual(['pending', 'running', 'failed']);
    expect(request.request.params.get('Offset')).toBe('0');
    expect(request.request.params.get('Limit')).toBe('50');
    expect(text(page.querySelector('h1'))).toBe('Jobs');
    expect(
      Array.from(page.querySelectorAll('[role="group"] button[aria-pressed="true"]')).map(text),
    ).toEqual(['Unfinished']);
  });

  it('shows every job when asked', async () => {
    const { page } = await openList('/jobs');

    buttonNamed(page, 'All').click();
    const request = await jobsRequest();
    request.flush(pageOf([aJob(4, { status: 'succeeded', attemptsMade: 1 }), waiting]));
    await vi.waitFor(() => expect(rows(page).length).toBe(2));

    expect(router.url).toBe('/jobs?show=all');
    expect(request.request.params.has('Status')).toBe(false);
    expect(
      Array.from(page.querySelectorAll('[role="group"] button[aria-pressed="true"]')).map(text),
    ).toEqual(['All']);
  });

  it('shows every job when the URL asks for all', async () => {
    const { request } = await openList('/jobs?show=all');

    expect(request.request.params.has('Status')).toBe(false);
  });

  it("shows each job's status, type, submitter and attempts", async () => {
    const { page } = await openList('/jobs');

    const created = (job: JobSummaryResponse) =>
      text(rows(page)[[waiting, running, failed].indexOf(job)].cells[4]);
    expect(rowTexts(page).map((cells) => cells.slice(0, 4))).toEqual([
      ['Pending', 'ingest', 'None', '0 of 3'],
      ['Running', 'ingest', 'Ada', '1 of 3'],
      ['Failed', 'ingest', 'Grace', '3 of 3'],
    ]);
    expect(created(waiting)).not.toBe('');
  });

  it('shows the error of a failed job under its row', async () => {
    const { page } = await openList('/jobs');

    expect(noteUnder(rows(page)[2])).toBe('The file ended too soon.');
    expect(noteUnder(rows(page)[0])).toBe('');
    expect(noteUnder(rows(page)[1])).toBe('');
  });

  it('offers Retry on a failed job and Delete on one that is not running', async () => {
    const { page } = await openList('/jobs');

    const [waitingRow, runningRow, failedRow] = rows(page);
    expect(buttonsOf(waitingRow)).toEqual(['Delete']);
    expect(buttonsOf(runningRow)).toEqual([]);
    expect(buttonsOf(failedRow)).toEqual(['Retry', 'Delete']);
  });

  it('retries a failed job and deletes a job through the API', async () => {
    const { page } = await openList('/jobs');

    buttonNamed(rows(page)[2], 'Retry').click();
    const retry = await vi.waitFor(() => httpTesting.expectOne(`/api/jobs/${failed.id}/retry`));
    expect(retry.request.method).toBe('POST');
    retry.flush(null, { status: 204, statusText: 'No Content' });

    buttonNamed(rows(page)[0], 'Delete').click();
    const deletion = await vi.waitFor(() => httpTesting.expectOne(`/api/jobs/${waiting.id}`));
    expect(deletion.request.method).toBe('DELETE');
    deletion.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('says so when a retry fails', async () => {
    const { page } = await openList('/jobs');

    buttonNamed(rows(page)[2], 'Retry').click();
    (await vi.waitFor(() => httpTesting.expectOne(`/api/jobs/${failed.id}/retry`))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });

    await vi.waitFor(() =>
      expect(text(page.querySelector('tbody [role="alert"]'))).toContain('Something went wrong'),
    );
  });

  it('changes a row when the hub says its job changed', async () => {
    const { page } = await openList('/jobs');

    hub.jobChanged.next({ ...waiting, status: 'running', attemptsMade: 1 });

    await vi.waitFor(() => expect(rowTexts(page)[0][0]).toBe('Running'));
    expect(rowTexts(page)[0][3]).toBe('1 of 3');
    expect(rows(page).length).toBe(3);
  });

  it('drops a row whose job no longer belongs on what is shown', async () => {
    const { page } = await openList('/jobs');

    hub.jobChanged.next({ ...running, status: 'succeeded' });

    await vi.waitFor(() => expect(rows(page).length).toBe(2));
    expect(page.textContent).toContain('2 jobs');
  });

  it('keeps a job that has succeeded when every job is shown', async () => {
    const { page } = await openList('/jobs?show=all');

    hub.jobChanged.next({ ...running, status: 'succeeded' });

    await vi.waitFor(() => expect(rowTexts(page)[1][0]).toBe('Succeeded'));
    expect(rows(page).length).toBe(3);
  });

  it('adds the row of a job the hub announces on the first page', async () => {
    const { page } = await openList('/jobs');

    hub.jobChanged.next(aJob(4, { submittedBy: 'Linus', createdAt: '2026-11-14T12:05:00Z' }));

    await vi.waitFor(() => expect(rows(page).length).toBe(4));
    expect(rowTexts(page)[0][2]).toBe('Linus');
    expect(page.textContent).toContain('4 jobs');
  });

  it('leaves the list and its count alone when a job from a later page changes', async () => {
    const { page } = await openList('/jobs?show=all', [waiting, running, failed], 120);

    hub.jobChanged.next(aJob(4, { status: 'running', createdAt: '2026-11-13T12:00:00Z' }));
    await Promise.resolve();

    expect(rows(page).length).toBe(3);
    expect(page.textContent).toContain('120 jobs');
  });

  it('applies what the hub said while the list was loading to the answer', async () => {
    const { page } = await openList('/jobs');

    hub.reconnected.next();
    const request = await jobsRequest();
    hub.jobChanged.next({ ...running, status: 'failed', error: 'It broke.' });
    hub.jobRemoved.next(waiting.id);
    request.flush(pageOf([waiting, running, failed]));

    await vi.waitFor(() =>
      expect(rowTexts(page).map((cells) => cells[0])).toEqual(['Failed', 'Failed']),
    );
    expect(buttonsOf(rows(page)[0])).toContain('Retry');
  });

  it('does not add the row of a job that does not belong on what is shown', async () => {
    const { page } = await openList('/jobs');

    hub.jobChanged.next(aJob(4, { status: 'succeeded' }));
    await Promise.resolve();

    expect(rows(page).length).toBe(3);
  });

  it('does not add the row of a job on a page after the first', async () => {
    const { page } = await openList('/jobs?page=2', [waiting], 51);

    hub.jobChanged.next(aJob(4));
    await Promise.resolve();

    expect(rows(page).length).toBe(1);
  });

  it('drops a row when the hub says its job was removed', async () => {
    const { page } = await openList('/jobs');

    hub.jobRemoved.next(waiting.id);

    await vi.waitFor(() => expect(rows(page).length).toBe(2));
    expect(rowTexts(page).map((cells) => cells[0])).toEqual(['Running', 'Failed']);
  });

  it('ignores the removal of a job that is not on the page', async () => {
    const { page } = await openList('/jobs');

    hub.jobRemoved.next('0198a1b2-0000-7000-8000-0000000000ff');
    await Promise.resolve();

    expect(rows(page).length).toBe(3);
  });

  it('asks again when the hub reconnects', async () => {
    const { page } = await openList('/jobs');

    hub.reconnected.next();
    (await jobsRequest()).flush(pageOf([failed]));

    await vi.waitFor(() => expect(rows(page).length).toBe(1));
  });

  it('pages through the list', async () => {
    const { page } = await openList('/jobs', [waiting, running], 120);
    expect(page.textContent).toContain('Page 1 of 3');
    expect(buttonNamed(page, 'Previous').disabled).toBe(true);

    buttonNamed(page, 'Next').click();
    const request = await jobsRequest();
    request.flush(pageOf([failed], 120));
    await vi.waitFor(() => expect(rows(page).length).toBe(1));

    expect(router.url).toBe('/jobs?page=2');
    expect(request.request.params.get('Offset')).toBe('50');
    expect(request.request.params.getAll('Status')).toEqual(['pending', 'running', 'failed']);
    expect(page.textContent).toContain('Page 2 of 3');
  });

  it('goes back to the first page when the filter changes', async () => {
    const { page } = await openList('/jobs?page=2', [failed], 120);

    buttonNamed(page, 'All').click();
    const request = await jobsRequest();
    request.flush(pageOf([waiting], 120));
    await vi.waitFor(() => expect(rows(page).length).toBe(1));

    expect(router.url).toBe('/jobs?show=all');
    expect(request.request.params.get('Offset')).toBe('0');
  });

  it('says there are no unfinished jobs when there are none', async () => {
    const { page } = await openList('/jobs', []);

    expect(page.querySelector('table')).toBeNull();
    expect(page.textContent).toContain('No unfinished jobs');
  });

  it('says there are no jobs when there are none at all', async () => {
    const { page } = await openList('/jobs?show=all', []);

    expect(page.textContent).toContain('No jobs yet');
  });

  it('offers to try again when the list fails to load', async () => {
    const navigating = harness.navigateByUrl('/jobs');
    (await jobsRequest()).flush(null, { status: 500, statusText: 'Server Error' });
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await shown(page);
    expect(page.querySelector('table')).toBeNull();

    buttonNamed(page, 'Retry').click();
    (await jobsRequest()).flush(pageOf([failed]));

    await vi.waitFor(() => expect(rows(page).length).toBe(1));
  });
});
