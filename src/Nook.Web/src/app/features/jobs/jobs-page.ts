import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideListChecks } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmToggleGroupImports } from '@spartan-ng/helm/toggle-group';
import { distinctUntilChanged, map } from 'rxjs';
import { JobsApi } from '../../api/jobs-api';
import { components } from '../../api/schema';
import { ActivityHub } from '../../core/activity-hub';
import { somethingWentWrong } from '../../core/messages';
import { reloadable } from '../../core/reloadable';
import { LoadFailed } from '../../ui/load-failed';
import { LoadingRegion } from '../../ui/loading';
import { pageHeading } from '../../ui/page-heading';

type JobStatus = components['schemas']['JobStatus'];
type JobSummaryResponse = components['schemas']['JobSummaryResponse'];
type PageOfJobSummaryResponse = components['schemas']['PageOfJobSummaryResponse'];

/** Which jobs the list holds: those that are not finished, or every one. */
type Show = 'unfinished' | 'all';

/** What the URL asks the list to show. */
interface Query {
  /** The page, counting from 1. */
  page: number;
  show: Show;
}

/** Where the list stands for the query the URL names. */
type Listing =
  | { status: 'loading' }
  | { status: 'loaded'; page: PageOfJobSummaryResponse }
  | { status: 'failed' };

const pageSize = 50;

/** The statuses of the jobs that are not finished: a job that has succeeded needs no one's attention. */
const unfinishedStatuses: JobStatus[] = ['pending', 'running', 'failed'];

const statusLabels: Record<JobStatus, string> = {
  pending: 'Pending',
  running: 'Running',
  succeeded: 'Succeeded',
  failed: 'Failed',
};

/** Whether a job in `status` belongs in the list that shows `show`. */
function belongs(show: Show, status: JobStatus): boolean {
  return show === 'all' || unfinishedStatuses.includes(status);
}

/**
 * The jobs the server runs, for an admin: one page at a time, newest first, opening on the jobs that are not
 * finished. A failed job can be run again, and a job that is not running can be deleted. Rows change as the activity
 * hub reports their jobs, a job made since the list was loaded joins the top of the first page, and the list is
 * loaded again when the hub reconnects.
 */
@Component({
  selector: 'app-jobs-page',
  imports: [
    DatePipe,
    LoadingRegion,
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    HlmEmptyImports,
    HlmSkeletonImports,
    HlmTableImports,
    HlmToggleGroupImports,
    LoadFailed,
  ],
  providers: [provideIcons({ lucideListChecks })],
  template: `
    @let shown = query();
    <div class="mb-6 flex flex-wrap items-center justify-between gap-2">
      <h1 [class]="pageHeading">Jobs</h1>
      <div
        hlmToggleGroup
        type="single"
        variant="outline"
        size="sm"
        aria-label="Show"
        [nullable]="false"
        [value]="shown.show"
        (valueChange)="showJobs($event)"
      >
        <button hlmToggleGroupItem type="button" value="unfinished">Unfinished</button>
        <button hlmToggleGroupItem type="button" value="all">All</button>
      </div>
    </div>
    @let current = listing();
    @if (current.status === 'loading') {
      <app-loading class="flex flex-col gap-3">
        @for (width of skeletonWidths; track $index) {
          <div hlmSkeleton class="h-4" [style.width.rem]="width" aria-hidden="true"></div>
        }
      </app-loading>
    } @else if (current.status === 'failed') {
      <app-load-failed (retry)="reload()" />
    } @else {
      @if (current.page.total === 0) {
        <div hlmEmpty class="border">
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideListChecks" /></div>
            <div hlmEmptyTitle>
              {{ shown.show === 'all' ? 'No jobs yet' : 'No unfinished jobs' }}
            </div>
            <p hlmEmptyDescription>
              {{
                shown.show === 'all'
                  ? 'The work Nook does in the background will be listed here.'
                  : 'Jobs that are waiting, running or failed will be listed here.'
              }}
            </p>
          </div>
        </div>
      } @else {
        <div hlmTableContainer>
          <table hlmTable>
            <thead hlmTHead>
              <tr hlmTr>
                <th hlmTh>Status</th>
                <th hlmTh>Type</th>
                <th hlmTh>Submitted by</th>
                <th hlmTh>Attempts</th>
                <th hlmTh>Created</th>
                <th hlmTh><span class="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody hlmTBody>
              @for (job of current.page.items; track job.id) {
                <tr hlmTr>
                  <td hlmTd>
                    <span hlmBadge [variant]="badgeVariant(job.status)">{{
                      statusLabels[job.status]
                    }}</span>
                  </td>
                  <td hlmTd>{{ job.type }}</td>
                  <td hlmTd>{{ job.submittedBy ?? 'None' }}</td>
                  <td hlmTd>{{ job.attemptsMade }} of {{ job.attemptsAllowed }}</td>
                  <td hlmTd>{{ job.createdAt | date: 'medium' }}</td>
                  <td hlmTd>
                    <div class="flex justify-end gap-2">
                      @if (job.status === 'failed') {
                        <button
                          hlmBtn
                          variant="outline"
                          size="sm"
                          type="button"
                          (click)="change(job.id, retryJob)"
                        >
                          Retry
                        </button>
                      }
                      @if (job.status !== 'running') {
                        <button
                          hlmBtn
                          variant="ghost"
                          size="sm"
                          type="button"
                          (click)="change(job.id, deleteJob)"
                        >
                          Delete
                        </button>
                      }
                    </div>
                  </td>
                </tr>
                @let problem = problems()[job.id];
                @if (job.status === 'failed' && job.error) {
                  <tr hlmTr>
                    <td
                      hlmTd
                      colspan="6"
                      class="text-destructive font-mono text-xs break-words whitespace-pre-wrap"
                    >
                      {{ job.error }}
                    </td>
                  </tr>
                }
                @if (problem) {
                  <tr hlmTr>
                    <td hlmTd colspan="6" class="text-destructive text-xs" role="alert">
                      {{ problem }}
                    </td>
                  </tr>
                }
              }
            </tbody>
          </table>
        </div>
        @let pageCount = pageCountOf(current.page.total);
        <nav class="mt-8 flex flex-wrap items-center justify-between gap-2" aria-label="Pages">
          <p class="text-muted-foreground text-sm">{{ totalText(current.page.total) }}</p>
          <div class="flex items-center gap-2">
            <span class="text-muted-foreground text-sm"
              >Page {{ shown.page }} of {{ pageCount }}</span
            >
            <button
              hlmBtn
              variant="outline"
              size="sm"
              type="button"
              [disabled]="shown.page <= 1"
              (click)="goToPage(shown.page - 1)"
            >
              Previous
            </button>
            <button
              hlmBtn
              variant="outline"
              size="sm"
              type="button"
              [disabled]="shown.page >= pageCount"
              (click)="goToPage(shown.page + 1)"
            >
              Next
            </button>
          </div>
        </nav>
      }
    }
  `,
})
export class JobsPage {
  protected readonly pageHeading = pageHeading;
  protected readonly statusLabels = statusLabels;
  private readonly jobsApi = inject(JobsApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  /** How wide, in rem, each line standing in for a job is while the list loads. */
  protected readonly skeletonWidths = [30, 28, 32, 26, 30];

  // The pager and the toggle change the URL without recreating the page, so the list follows the URL rather than
  // reading it once, and an answer for a query the user has already left is dropped.
  private readonly query$ = this.route.queryParamMap.pipe(
    map(toQuery),
    distinctUntilChanged((a, b) => a.page === b.page && a.show === b.show),
  );

  /** What the URL asks the list to show. */
  protected readonly query = toSignal(this.query$, { requireSync: true });

  private readonly loading = reloadable(this.query$, (query) => this.load(query));

  /** What the server answered, then changed by what the hub says until the next load. */
  protected readonly listing = signal<Listing>({ status: 'loading' });

  /** What the hub said while the list was on its way, to be applied to the answer. */
  private heardWhileLoading: (() => void)[] = [];

  /** Asks again for what failed to load. */
  protected readonly reload = this.loading.reload;

  /** What went wrong with a retry or a deletion, by the id of the job it was for. */
  protected readonly problems = signal<Record<string, string>>({});

  protected readonly retryJob = (id: string) => this.jobsApi.retry(id);
  protected readonly deleteJob = (id: string) => this.jobsApi.delete(id);

  constructor() {
    const hub = inject(ActivityHub);
    this.loading.state$.pipe(takeUntilDestroyed()).subscribe((state) => {
      this.listing.set(state);
      const heard = this.heardWhileLoading;
      this.heardWhileLoading = [];
      if (state.status === 'loaded') {
        // The server may have read the list before a change the hub has already told of, so each is applied again.
        heard.forEach((apply) => apply());
      }
    });
    hub.jobChanged
      .pipe(takeUntilDestroyed())
      .subscribe((job) => this.hear(() => this.applyChange(job)));
    hub.jobRemoved
      .pipe(takeUntilDestroyed())
      .subscribe((id) => this.hear(() => this.applyRemoval(id)));
    // What the hub said while it was away is lost, so the list is what the server holds again.
    hub.reconnected.pipe(takeUntilDestroyed()).subscribe(() => this.reload());
  }

  protected badgeVariant(status: JobStatus): 'default' | 'secondary' | 'destructive' | 'outline' {
    switch (status) {
      case 'failed':
        return 'destructive';
      case 'running':
        return 'default';
      case 'pending':
        return 'secondary';
      default:
        return 'outline';
    }
  }

  /** Shows the jobs `value` names, from the first page. */
  protected showJobs(value: unknown): void {
    if (value === 'unfinished' || value === 'all') {
      this.show({ page: 1, show: value });
    }
  }

  protected goToPage(page: number): void {
    this.show({ page, show: this.query().show });
  }

  protected pageCountOf(total: number): number {
    return Math.max(1, Math.ceil(total / pageSize));
  }

  protected totalText(total: number): string {
    return total === 1 ? '1 job' : `${total} jobs`;
  }

  /** Runs `call` for a job. The row itself changes through the hub, so only a failure is shown here. */
  protected async change(id: string, call: (id: string) => Promise<void>): Promise<void> {
    this.problems.update((problems) => ({ ...problems, [id]: '' }));
    try {
      await call(id);
    } catch {
      this.problems.update((problems) => ({ ...problems, [id]: somethingWentWrong }));
    }
  }

  /** Puts the query in the URL, leaving out what is the default. */
  private show({ page, show }: Query): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { page: page === 1 ? null : page, show: show === 'all' ? 'all' : null },
      queryParamsHandling: 'merge',
    });
  }

  private async load({ page, show }: Query): Promise<Listing> {
    try {
      const response = await this.jobsApi.list({
        Offset: (page - 1) * pageSize,
        Limit: pageSize,
        Status: show === 'all' ? undefined : unfinishedStatuses,
      });
      return { status: 'loaded', page: response };
    } catch {
      return { status: 'failed' };
    }
  }

  /** Applies what the hub said to the list, or keeps it for the answer when the list is on its way. */
  private hear(apply: () => void): void {
    if (this.listing().status === 'loading') {
      this.heardWhileLoading.push(apply);
    } else {
      apply();
    }
  }

  /**
   * Changes the row of a job the hub reported, drops it when it no longer belongs on what is shown, and adds it to
   * the top of the first page when it belongs and was made after every job there. An older job that is not on the
   * page is on a later one, and already counted.
   */
  private applyChange(job: JobSummaryResponse): void {
    const { page, show } = this.query();
    this.listing.update((current) => {
      if (current.status !== 'loaded') {
        return current;
      }
      const { items, total } = current.page;
      const onPage = items.some((item) => item.id === job.id);
      if (onPage) {
        return belongs(show, job.status)
          ? withItems(
              current.page,
              items.map((item) => (item.id === job.id ? job : item)),
              total,
            )
          : withItems(
              current.page,
              items.filter((item) => item.id !== job.id),
              total - 1,
            );
      }
      const newest = items.at(0);
      const madeSince = !newest || Date.parse(job.createdAt) > Date.parse(newest.createdAt);
      return belongs(show, job.status) && page === 1 && madeSince
        ? withItems(current.page, [job, ...items].slice(0, pageSize), total + 1)
        : current;
    });
  }

  /** Drops the row of a job the hub reported deleted. */
  private applyRemoval(id: string): void {
    this.listing.update((current) =>
      current.status === 'loaded' && current.page.items.some((item) => item.id === id)
        ? withItems(
            current.page,
            current.page.items.filter((item) => item.id !== id),
            current.page.total - 1,
          )
        : current,
    );
  }
}

/** `page` holding `items`, out of `total` in all. */
function withItems(
  page: PageOfJobSummaryResponse,
  items: JobSummaryResponse[],
  total: number,
): Listing {
  return { status: 'loaded', page: { ...page, items, total } };
}

/** Reads the query from the URL: the first page for a page that is not a whole number above 0, and the jobs not finished unless it says `all`. */
function toQuery(params: ParamMap): Query {
  const pageParam = params.get('page');
  return {
    page: pageParam !== null && /^[1-9][0-9]*$/.test(pageParam) ? Number(pageParam) : 1,
    show: params.get('show') === 'all' ? 'all' : 'unfinished',
  };
}
