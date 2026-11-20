import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideScrollText } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { distinctUntilChanged, map } from 'rxjs';
import { AuditLogApi } from '../../api/audit-log-api';
import { components } from '../../api/schema';
import { reloadable } from '../../core/reloadable';
import { LoadFailed } from '../../ui/load-failed';
import { LoadingRegion } from '../../ui/loading';
import { pageHeading } from '../../ui/page-heading';
import { actionLabel } from './action-labels';

type PageOfAuditEntryResponse = components['schemas']['PageOfAuditEntryResponse'];
type AuditFiltersResponse = components['schemas']['AuditFiltersResponse'];

/** What the URL asks the log to show. */
interface Query {
  /** The page, counting from 1. */
  page: number;
  /** The id of the person whose entries are shown, or null for everyone's. */
  person: string | null;
  /** The name of the action whose entries are shown, or null for every action's. */
  action: string | null;
}

/** Where the log stands for the query the URL names. */
type Listing =
  | { status: 'loading' }
  | { status: 'loaded'; page: PageOfAuditEntryResponse }
  | { status: 'failed' };

const pageSize = 50;

/** The choice of a filter that leaves it out. It is no person's id and no action's name. */
const anyChoice = '*';

/** The kind of subject an entry names when it names a work. */
const workSubjectType = 'work';

/**
 * The audit log, for an admin: who did what, one page at a time, newest first, narrowed by person and by action. The
 * page only reads, and does not follow the log as it grows.
 */
@Component({
  selector: 'app-audit-log-page',
  imports: [
    DatePipe,
    LoadingRegion,
    NgIcon,
    RouterLink,
    HlmButtonImports,
    HlmEmptyImports,
    HlmSelectImports,
    HlmSkeletonImports,
    HlmTableImports,
    LoadFailed,
  ],
  providers: [provideIcons({ lucideScrollText })],
  template: `
    @let shown = query();
    <div class="mb-6 flex flex-wrap items-center justify-between gap-2">
      <h1 [class]="pageHeading">Audit log</h1>
      <div class="flex flex-wrap items-center gap-2">
        <label class="sr-only" for="audit-log-person">Person</label>
        <hlm-select
          [value]="shown.person ?? anyChoice"
          [itemToString]="personLabel"
          (valueChange)="choosePerson($event)"
        >
          <hlm-select-trigger size="sm" class="w-48" buttonId="audit-log-person">
            <hlm-select-value />
          </hlm-select-trigger>
          <hlm-select-content *hlmSelectPortal>
            <hlm-select-group>
              <hlm-select-item [value]="anyChoice">Everyone</hlm-select-item>
              @for (actor of filters().actors; track actor.id) {
                <hlm-select-item [value]="actor.id">{{ actor.name }}</hlm-select-item>
              }
            </hlm-select-group>
          </hlm-select-content>
        </hlm-select>
        <label class="sr-only" for="audit-log-action">Action</label>
        <hlm-select
          [value]="shown.action ?? anyChoice"
          [itemToString]="actionChoiceLabel"
          (valueChange)="chooseAction($event)"
        >
          <hlm-select-trigger size="sm" class="w-56" buttonId="audit-log-action">
            <hlm-select-value />
          </hlm-select-trigger>
          <hlm-select-content *hlmSelectPortal>
            <hlm-select-group>
              <hlm-select-item [value]="anyChoice">Every action</hlm-select-item>
              @for (action of filters().actions; track action) {
                <hlm-select-item [value]="action">{{ actionLabel(action) }}</hlm-select-item>
              }
            </hlm-select-group>
          </hlm-select-content>
        </hlm-select>
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
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideScrollText" /></div>
            <div hlmEmptyTitle>
              {{ shown.person || shown.action ? 'No entries match' : 'Nothing logged yet' }}
            </div>
            <p hlmEmptyDescription>
              {{
                shown.person || shown.action
                  ? 'Try another person or action.'
                  : 'What people do that Nook keeps a record of will be listed here.'
              }}
            </p>
          </div>
        </div>
      } @else {
        <div hlmTableContainer>
          <table hlmTable>
            <thead hlmTHead>
              <tr hlmTr>
                <th hlmTh>Time</th>
                <th hlmTh>Person</th>
                <th hlmTh>Action</th>
                <th hlmTh>Subject</th>
              </tr>
            </thead>
            <tbody hlmTBody>
              @for (entry of current.page.items; track entry.id) {
                <tr hlmTr>
                  <td hlmTd class="whitespace-nowrap">{{ entry.at | date: 'medium' }}</td>
                  <td hlmTd>
                    <div>{{ entry.actorName }}</div>
                    <div class="text-muted-foreground text-xs">{{ entry.actorEmail }}</div>
                  </td>
                  <td hlmTd>{{ actionLabel(entry.action) }}</td>
                  <td hlmTd>
                    @if (entry.subjectType === workSubjectType && entry.subjectId) {
                      <a
                        class="font-medium hover:underline"
                        [routerLink]="['/works', entry.subjectId]"
                        >{{ entry.subjectName }}</a
                      >
                    } @else {
                      {{ entry.subjectName }}
                    }
                  </td>
                </tr>
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
export class AuditLogPage {
  protected readonly pageHeading = pageHeading;
  protected readonly anyChoice = anyChoice;
  protected readonly workSubjectType = workSubjectType;
  protected readonly actionLabel = actionLabel;
  private readonly auditLogApi = inject(AuditLogApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  /** How wide, in rem, each line standing in for an entry is while the log loads. */
  protected readonly skeletonWidths = [30, 28, 32, 26, 30];

  // The pager and the filters change the URL without recreating the page, so the log follows the URL rather than
  // reading it once, and an answer for a query the user has already left is dropped.
  private readonly query$ = this.route.queryParamMap.pipe(
    map(toQuery),
    distinctUntilChanged(
      (a, b) => a.page === b.page && a.person === b.person && a.action === b.action,
    ),
  );

  /** What the URL asks the log to show. */
  protected readonly query = toSignal(this.query$, { requireSync: true });

  private readonly loading = reloadable(this.query$, (query) => this.load(query));

  /** What the server answered for the query the URL names. */
  protected readonly listing = toSignal(this.loading.state$, { requireSync: true });

  /** Asks again for what failed to load. */
  protected readonly reload = this.loading.reload;

  /** The people and actions the log can be narrowed by: none until they have loaded, and none if they fail to. */
  protected readonly filters = signal<AuditFiltersResponse>({ actors: [], actions: [] });

  constructor() {
    void this.loadFilters();
  }

  /** The words a person's choice reads as in the closed select: their name, or "Everyone". */
  protected readonly personLabel = (value: unknown): string =>
    value === anyChoice || value === null || value === undefined
      ? 'Everyone'
      : (this.filters().actors.find((actor) => actor.id === value)?.name ?? String(value));

  /** The words an action's choice reads as in the closed select. */
  protected readonly actionChoiceLabel = (value: unknown): string =>
    value === anyChoice || value === null || value === undefined
      ? 'Every action'
      : actionLabel(String(value));

  /** Narrows the log to the person `value` names, or to everyone, from the first page. */
  protected choosePerson(value: unknown): void {
    if (typeof value === 'string') {
      this.show({ ...this.query(), page: 1, person: value === anyChoice ? null : value });
    }
  }

  /** Narrows the log to the action `value` names, or to every action, from the first page. */
  protected chooseAction(value: unknown): void {
    if (typeof value === 'string') {
      this.show({ ...this.query(), page: 1, action: value === anyChoice ? null : value });
    }
  }

  protected goToPage(page: number): void {
    this.show({ ...this.query(), page });
  }

  protected pageCountOf(total: number): number {
    return Math.max(1, Math.ceil(total / pageSize));
  }

  protected totalText(total: number): string {
    return total === 1 ? '1 entry' : `${total} entries`;
  }

  /** Puts the query in the URL, leaving out what is the default. */
  private show({ page, person, action }: Query): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { page: page === 1 ? null : page, person, action },
      queryParamsHandling: 'merge',
    });
  }

  private async load({ page, person, action }: Query): Promise<Listing> {
    try {
      const response = await this.auditLogApi.list({
        Offset: (page - 1) * pageSize,
        Limit: pageSize,
        ActorId: person ?? undefined,
        Action: action ?? undefined,
      });
      return { status: 'loaded', page: response };
    } catch {
      return { status: 'failed' };
    }
  }

  private async loadFilters(): Promise<void> {
    try {
      this.filters.set(await this.auditLogApi.filters());
    } catch {
      // The log can still be read and paged without the lists to narrow it by.
    }
  }
}

/** Reads the query from the URL: the first page for a page that is not a whole number above 0, and no filter for one that is empty. */
function toQuery(params: ParamMap): Query {
  const pageParam = params.get('page');
  return {
    page: pageParam !== null && /^[1-9][0-9]*$/.test(pageParam) ? Number(pageParam) : 1,
    person: params.get('person') || null,
    action: params.get('action') || null,
  };
}
