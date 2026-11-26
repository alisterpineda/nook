import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowDown, lucideArrowUp, lucideEllipsis } from '@ng-icons/lucide';
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
import { HlmDialogService } from '@spartan-ng/helm/dialog';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { distinctUntilChanged, map } from 'rxjs';
import { UsersApi } from '../../api/users-api';
import { components } from '../../api/schema';
import { reloadable } from '../../core/reloadable';
import { Session } from '../../core/session';
import { AddUserContext, AddUserDialog } from './add-user-dialog';
import {
  ChangeUserRoleContext,
  ChangeUserRoleDialog,
  isAssignableRole,
} from './change-user-role-dialog';
import { DeleteUserContext, DeleteUserDialog } from './delete-user-dialog';
import { SetUserPasswordContext, SetUserPasswordDialog } from './set-user-password-dialog';
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
  { id: 'actions', header: '', enableSorting: false },
];

/** Nothing to list while the users load. A single array, so the table is not handed new data on every pass. */
const noUsers: UserSummaryResponse[] = [];

/** The people who have an account, for an admin, who can add one while Nook signs in with passwords. */
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
    HlmDropdownMenuImports,
    HlmSkeletonImports,
    HlmTableImports,
    LoadFailed,
  ],
  providers: [provideIcons({ lucideArrowDown, lucideArrowUp, lucideEllipsis })],
  template: `
    <div class="mb-6 flex flex-wrap items-center justify-between gap-2">
      <h1 [class]="pageHeading">Users</h1>
      @if (passwordSignIn()) {
        <button hlmBtn type="button" (click)="addUser()">Add user</button>
      }
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
                      @case ('role') {
                        {{ cell.row.original.role }}
                        @if (cell.row.original.roleFromSingleSignOn) {
                          <span class="text-muted-foreground block text-xs"
                            >Set by single sign-on</span
                          >
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
                            routerLink="/admin/audit-log"
                            [queryParams]="{ person: cell.row.original.id }"
                            [attr.aria-label]="'Audit log for ' + cell.row.original.name"
                            >Audit log</a
                          >
                        }
                      }
                      @case ('actions') {
                        @if (hasActions(cell.row.original)) {
                          <button
                            hlmBtn
                            variant="ghost"
                            size="icon"
                            type="button"
                            [attr.aria-label]="'Actions for ' + cell.row.original.name"
                            [hlmDropdownMenuTrigger]="userMenu"
                          >
                            <ng-icon name="lucideEllipsis" aria-hidden="true" />
                          </button>
                          <ng-template #userMenu>
                            <hlm-dropdown-menu class="min-w-40">
                              @if (passwordSignIn()) {
                                <button
                                  hlmDropdownMenuItem
                                  type="button"
                                  (triggered)="setPassword(cell.row.original)"
                                >
                                  Set password
                                </button>
                              }
                              @if (!cell.row.original.roleFromSingleSignOn) {
                                <button
                                  hlmDropdownMenuItem
                                  type="button"
                                  (triggered)="changeRole(cell.row.original)"
                                >
                                  Change role
                                </button>
                              }
                              <div hlmDropdownMenuSeparator></div>
                              <button
                                hlmDropdownMenuItem
                                variant="destructive"
                                type="button"
                                (triggered)="deleteUser(cell.row.original)"
                              >
                                Delete
                              </button>
                            </hlm-dropdown-menu>
                          </ng-template>
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
  private readonly dialogs = inject(HlmDialogService);

  /** Whether Nook signs in with passwords, which is when an admin can add a user. False until known, and when unknown. */
  protected readonly passwordSignIn = signal(false);

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

  constructor() {
    // A failed read leaves the button out: the page is still useful without it.
    this.session.signInOptions().then(
      (options) => this.passwordSignIn.set(options.passwordSignIn),
      () => undefined,
    );
  }

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

  /** Opens the dialog that adds a user, and shows the list again once one is added. */
  protected addUser(): void {
    const context: AddUserContext = { added: () => this.reload() };
    this.dialogs.open(AddUserDialog, { context, contentClass: 'sm:max-w-md' });
  }

  /** Whether a user's row has a menu: another user's always holds Delete, and one's own never has a menu. */
  protected hasActions(user: UserSummaryResponse): boolean {
    return user.id !== this.session.user()?.id;
  }

  /** Opens the dialog that changes a user's role, and shows the list again once it is changed or the user turns out to be gone. */
  protected changeRole(user: UserSummaryResponse): void {
    const role = user.builtInRole;
    const context: ChangeUserRoleContext = {
      id: user.id,
      name: user.name,
      role: isAssignableRole(role) ? role : null,
      changed: () => this.reload(),
    };
    this.dialogs.open(ChangeUserRoleDialog, { context, contentClass: 'sm:max-w-md' });
  }

  /** Opens the dialog that confirms deleting a user, and shows the list again once they are deleted or turn out to be gone. */
  protected deleteUser(user: UserSummaryResponse): void {
    const context: DeleteUserContext = {
      id: user.id,
      name: user.name,
      email: user.email,
      deleted: () => this.reload(),
    };
    this.dialogs.open(DeleteUserDialog, { context, contentClass: 'sm:max-w-md' });
  }

  /** Opens the dialog that sets a user's password, and shows the list again once it is set or the user turns out to be gone. */
  protected setPassword(user: UserSummaryResponse): void {
    const context: SetUserPasswordContext = {
      id: user.id,
      name: user.name,
      changed: () => this.reload(),
    };
    this.dialogs.open(SetUserPasswordDialog, { context, contentClass: 'sm:max-w-md' });
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
