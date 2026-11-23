import { DatePipe } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowDown, lucideArrowUp } from '@ng-icons/lucide';
import {
  ColumnDef,
  FlexRender,
  Header,
  SortingState,
  injectTable,
  rowSortingFeature,
  tableFeatures,
} from '@tanstack/angular-table';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { distinctUntilChanged, map } from 'rxjs';
import { UsersApi } from '../../api/users-api';
import { components } from '../../api/schema';
import { reloadable } from '../../core/reloadable';
import { Session } from '../../core/session';
import { LoadFailed } from '../../ui/load-failed';
import { LoadingRegion } from '../../ui/loading';
import { pageHeading } from '../../ui/page-heading';

type PageOfUserSummaryResponse = components['schemas']['PageOfUserSummaryResponse'];
type UserSummaryResponse = components['schemas']['UserSummaryResponse'];

type Sort = 'name' | 'email' | 'role' | 'joined';
type Dir = 'asc' | 'desc';

/** What the URL asks the list to show. */
interface Query {
  /** The page, counting from 1. */
  page: number;
  /** The column the list is sorted by. */
  sort: Sort;
  /** The direction of the sort. */
  dir: Dir;
}

/** Where the list stands. */
type Listing =
  | { status: 'loading' }
  | { status: 'loaded'; page: PageOfUserSummaryResponse }
  | { status: 'failed' };

const pageSize = 50;

const sorts: readonly Sort[] = ['name', 'email', 'role', 'joined'];

const features = tableFeatures({ rowSortingFeature });

/** What a user's stored credentials read as: nothing for an account with neither a password nor a linked login. */
function signInWords(user: UserSummaryResponse): string {
  if (user.hasPassword && user.hasSingleSignOn) {
    return 'Password and single sign-on';
  }
  if (user.hasPassword) {
    return 'Password';
  }
  return user.hasSingleSignOn ? 'Single sign-on' : '';
}

const columns: ColumnDef<typeof features, UserSummaryResponse>[] = [
  { id: 'name', header: 'Name', accessorFn: (user) => user.name },
  { id: 'email', header: 'Email', accessorFn: (user) => user.email },
  { id: 'role', header: 'Role', accessorFn: (user) => user.role },
  { id: 'joined', header: 'Joined', accessorFn: (user) => user.joinedAt },
  { id: 'signIn', header: 'Sign-in', accessorFn: signInWords, enableSorting: false },
  { id: 'audit', header: '', enableSorting: false },
];

/** Nothing to list while the users load. A single array, so the table is not handed new data on every pass. */
const noUsers: UserSummaryResponse[] = [];

/** The people who have an account, for an admin. The page only reads. */
@Component({
  selector: 'app-users-page',
  imports: [
    DatePipe,
    FlexRender,
    LoadingRegion,
    NgIcon,
    RouterLink,
    HlmBadgeImports,
    HlmButtonImports,
    HlmSkeletonImports,
    HlmTableImports,
    LoadFailed,
  ],
  providers: [provideIcons({ lucideArrowDown, lucideArrowUp })],
  template: `
    <div class="mb-6">
      <h1 [class]="pageHeading">Users</h1>
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
      <div hlmTableContainer>
        <table hlmTable>
          <thead hlmTHead>
            @for (headerGroup of table.getHeaderGroups(); track headerGroup.id) {
              <tr hlmTr>
                @for (header of headerGroup.headers; track header.id) {
                  <th hlmTh [attr.aria-sort]="ariaSort(header)">
                    @if (header.column.getCanSort()) {
                      <button
                        type="button"
                        class="inline-flex items-center gap-1 font-medium"
                        (click)="header.column.getToggleSortingHandler()?.($event)"
                      >
                        <ng-container *flexRenderHeader="header; let value">{{
                          value
                        }}</ng-container>
                        @switch (header.column.getIsSorted()) {
                          @case ('asc') {
                            <ng-icon name="lucideArrowUp" size="14" aria-hidden="true" />
                          }
                          @case ('desc') {
                            <ng-icon name="lucideArrowDown" size="14" aria-hidden="true" />
                          }
                        }
                      </button>
                    } @else {
                      <ng-container *flexRenderHeader="header; let value">{{ value }}</ng-container>
                    }
                  </th>
                }
              </tr>
            }
          </thead>
          <tbody hlmTBody>
            @for (row of table.getRowModel().rows; track row.id) {
              <tr hlmTr>
                @for (cell of row.getAllCells(); track cell.id) {
                  <td hlmTd>
                    @switch (cell.column.id) {
                      @case ('name') {
                        <span class="font-medium">{{ cell.row.original.name }}</span>
                        @if (cell.row.original.id === session.user()?.id) {
                          <span hlmBadge variant="secondary" class="ml-2">You</span>
                        }
                      }
                      @case ('joined') {
                        <span class="whitespace-nowrap">{{
                          cell.row.original.joinedAt | date: 'mediumDate'
                        }}</span>
                      }
                      @case ('audit') {
                        @if (cell.row.original.hasAuditEntries) {
                          <a
                            class="font-medium hover:underline"
                            routerLink="/audit-log"
                            [queryParams]="{ person: cell.row.original.id }"
                            [attr.aria-label]="'Audit log for ' + cell.row.original.name"
                            >Audit log</a
                          >
                        }
                      }
                      @default {
                        <ng-container *flexRenderCell="cell; let value">{{ value }}</ng-container>
                      }
                    }
                  </td>
                }
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
            >Page {{ query().page }} of {{ pageCount }}</span
          >
          <button
            hlmBtn
            variant="outline"
            size="sm"
            type="button"
            [disabled]="query().page <= 1"
            (click)="goToPage(query().page - 1)"
          >
            Previous
          </button>
          <button
            hlmBtn
            variant="outline"
            size="sm"
            type="button"
            [disabled]="query().page >= pageCount"
            (click)="goToPage(query().page + 1)"
          >
            Next
          </button>
        </div>
      </nav>
    }
  `,
})
export class UsersPage {
  protected readonly pageHeading = pageHeading;
  protected readonly session = inject(Session);
  private readonly usersApi = inject(UsersApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  /** How wide, in rem, each line standing in for a user is while the users load. */
  protected readonly skeletonWidths = [30, 28, 32, 26, 30];

  // Sorting and paging change the URL without recreating the page, so the list follows the URL rather than reading it
  // once, and an answer for a query the user has already left is dropped.
  private readonly query$ = this.route.queryParamMap.pipe(
    map(toQuery),
    distinctUntilChanged((a, b) => a.page === b.page && a.sort === b.sort && a.dir === b.dir),
  );

  /** What the URL asks the list to show. */
  protected readonly query = toSignal(this.query$, { requireSync: true });

  private readonly loading = reloadable(this.query$, (query) => this.load(query));

  /** What the server answered. */
  protected readonly listing = toSignal(this.loading.state$, { requireSync: true });

  /** Asks again for what failed to load. */
  protected readonly reload = this.loading.reload;

  private readonly users = computed(() => {
    const current = this.listing();
    return current.status === 'loaded' ? current.page.items : noUsers;
  });

  protected readonly table = injectTable(() => ({
    features,
    columns,
    data: this.users(),
    manualSorting: true,
    enableSortingRemoval: false,
    enableMultiSort: false,
    sortDescFirst: false,
    state: { sorting: [{ id: this.query().sort, desc: this.query().dir === 'desc' }] },
    onSortingChange: (updater) => {
      const current: SortingState = [{ id: this.query().sort, desc: this.query().dir === 'desc' }];
      const [next] = typeof updater === 'function' ? updater(current) : updater;
      if (next && isSort(next.id)) {
        this.show({ page: 1, sort: next.id, dir: next.desc ? 'desc' : 'asc' });
      }
    },
  }));

  /** How a header's sort reads to assistive technology: nothing for a column the list is not sorted by. */
  protected ariaSort(header: Header<typeof features, UserSummaryResponse, unknown>): string | null {
    const sorted = header.column.getIsSorted();
    return sorted === 'asc' ? 'ascending' : sorted === 'desc' ? 'descending' : null;
  }

  protected goToPage(page: number): void {
    this.show({ ...this.query(), page });
  }

  protected pageCountOf(total: number): number {
    return Math.max(1, Math.ceil(total / pageSize));
  }

  protected totalText(total: number): string {
    return total === 1 ? '1 user' : `${total} users`;
  }

  /** Puts the query in the URL, leaving out what is the default. */
  private show({ page, sort, dir }: Query): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        sort: sort === 'name' ? null : sort,
        dir: dir === 'asc' ? null : dir,
        page: page === 1 ? null : page,
      },
      queryParamsHandling: 'merge',
    });
  }

  private async load({ page, sort, dir }: Query): Promise<Listing> {
    try {
      const response = await this.usersApi.list({
        Offset: (page - 1) * pageSize,
        Limit: pageSize,
        Sort: sort,
        Direction: dir,
      });
      return { status: 'loaded', page: response };
    } catch {
      return { status: 'failed' };
    }
  }
}

function isSort(value: string | null): value is Sort {
  return sorts.some((sort) => sort === value);
}

/** Reads the query from the URL: the default for a sort or direction it does not know, and the first page for a page that is not a whole number above 0. */
function toQuery(params: ParamMap): Query {
  const sort = params.get('sort');
  const pageParam = params.get('page');
  return {
    page: pageParam !== null && /^[1-9][0-9]*$/.test(pageParam) ? Number(pageParam) : 1,
    sort: isSort(sort) ? sort : 'name',
    dir: params.get('dir') === 'desc' ? 'desc' : 'asc',
  };
}
