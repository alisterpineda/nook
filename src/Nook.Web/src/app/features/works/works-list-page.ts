import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideLibraryBig, lucideSearchX, lucideUpload } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmToggleGroupImports } from '@spartan-ng/helm/toggle-group';
import { distinctUntilChanged, map } from 'rxjs';
import { reloadable } from '../../core/reloadable';
import { components } from '../../api/schema';
import { WorksApi } from '../../api/works-api';
import { readTerm } from '../../core/search-term';
import { LibraryArea, areaParam, readArea, sameArea } from '../../core/library-area';
import { AddBooks } from '../upload/add-books';
import { Sections, libraryName } from '../../core/sections';
import { Session } from '../../core/session';
import { LoadFailed } from '../../ui/load-failed';
import { pageHeading } from '../../ui/page-heading';
import { WorkCard } from './work-card';
import { workGrid } from './work-grid';
import { WorkGridSkeleton } from './work-grid-skeleton';

type PageOfWorkSummaryResponse = components['schemas']['PageOfWorkSummaryResponse'];
type WorkSort = NonNullable<components['schemas']['WorkSort']>;

/** What the URL asks the list to show. */
interface ListQuery {
  area: LibraryArea;
  /** The words searched for, trimmed, or null when the URL names none. */
  term: string | null;
  sort: WorkSort;
  /** Counts from 1. */
  page: number;
}

/** Where the list stands for the query the URL names. */
type Listing =
  | { status: 'loading' }
  | { status: 'loaded'; page: PageOfWorkSummaryResponse }
  | { status: 'failed' };

const pageSize = 50;

/** The sort order the list takes when the URL names none: best match first during a search, by title otherwise. */
function defaultSortFor(term: string | null): WorkSort {
  return term === null ? 'title' : 'relevance';
}

const relevanceOption = { value: 'relevance', label: 'Relevance' } as const;

const sortOptions: readonly { value: WorkSort; label: string }[] = [
  { value: 'title', label: 'Title' },
  { value: 'added', label: 'Recently added' },
];

/** The sort orders the list offers, in the order the control shows them: Relevance only during a search. */
function sortOptionsFor(term: string | null): readonly { value: WorkSort; label: string }[] {
  return term === null ? sortOptions : [relevanceOption, ...sortOptions];
}

/**
 * One page of the works in the section the URL names, of the works in no section when it names the Default area,
 * or of every work the user can see when it names none, in the sort order and at the page the URL names. Its heading
 * names the section, or the words searched for.
 */
@Component({
  selector: 'app-works-list-page',
  imports: [
    NgIcon,
    RouterLink,
    HlmButtonImports,
    HlmEmptyImports,
    HlmToggleGroupImports,
    LoadFailed,
    WorkCard,
    WorkGridSkeleton,
  ],
  providers: [provideIcons({ lucideLibraryBig, lucideSearchX, lucideUpload })],
  template: `
    @let shownQuery = query();
    <div class="mb-6 flex flex-wrap items-center justify-between gap-2">
      <h1 [class]="pageHeading">{{ heading() }}</h1>
      <div
        hlmToggleGroup
        type="single"
        variant="outline"
        size="sm"
        aria-label="Sort by"
        [nullable]="false"
        [value]="shownQuery.sort"
        (valueChange)="sortBy($event)"
      >
        @for (option of sortOptionsFor(shownQuery.term); track option.value) {
          <button hlmToggleGroupItem type="button" [value]="option.value">
            {{ option.label }}
          </button>
        }
      </div>
    </div>
    @let current = listing();
    @if (current.status === 'loading') {
      <app-work-grid-skeleton />
    } @else if (current.status === 'failed') {
      <app-load-failed (retry)="reload()" />
    } @else {
      @if (current.page.total === 0) {
        <div hlmEmpty class="border">
          @if (shownQuery.term === null) {
            <div hlmEmptyHeader>
              <div hlmEmptyMedia variant="icon"><ng-icon name="lucideLibraryBig" /></div>
              <div hlmEmptyTitle>No works yet</div>
              <p hlmEmptyDescription>Books added here will show up on this page.</p>
            </div>
            @if (canUpload()) {
              <div hlmEmptyContent>
                <button hlmBtn size="sm" type="button" (click)="addBooks()">
                  <ng-icon name="lucideUpload" />
                  Add books
                </button>
              </div>
            }
          } @else {
            <div hlmEmptyHeader>
              <div hlmEmptyMedia variant="icon"><ng-icon name="lucideSearchX" /></div>
              <div hlmEmptyTitle>No works match "{{ shownQuery.term }}"</div>
              <p hlmEmptyDescription>Check the spelling, or try fewer or different words.</p>
            </div>
            <div hlmEmptyContent>
              <a hlmBtn variant="outline" size="sm" routerLink="/library">Clear search</a>
            </div>
          }
        </div>
      } @else {
        <ul [class]="workGrid" aria-label="Works">
          @for (work of current.page.items; track work.id) {
            <li>
              <app-work-card [work]="work" />
            </li>
          }
        </ul>
        @let pageCount = pageCountOf(current.page.total);
        <nav class="mt-8 flex flex-wrap items-center justify-between gap-2" aria-label="Pages">
          <p class="text-muted-foreground text-sm">
            {{ totalText(current.page.total, shownQuery.term !== null) }}
          </p>
          <div class="flex items-center gap-2">
            <span class="text-muted-foreground text-sm"
              >Page {{ shownQuery.page }} of {{ pageCount }}</span
            >
            <button
              hlmBtn
              variant="outline"
              size="sm"
              type="button"
              [disabled]="shownQuery.page <= 1"
              (click)="goToPage(shownQuery.page - 1)"
            >
              Previous
            </button>
            <button
              hlmBtn
              variant="outline"
              size="sm"
              type="button"
              [disabled]="shownQuery.page >= pageCount"
              (click)="goToPage(shownQuery.page + 1)"
            >
              Next
            </button>
          </div>
        </nav>
      }
    }
  `,
})
export class WorksListPage {
  protected readonly pageHeading = pageHeading;
  private readonly worksApi = inject(WorksApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  private readonly sections = inject(Sections);

  private readonly addBooksDialog = inject(AddBooks);

  protected readonly canUpload = inject(Session).canUpload;
  protected readonly workGrid = workGrid;
  protected readonly sortOptionsFor = sortOptionsFor;

  // The sidebar, the sort control and the pager all change the URL without recreating the page, so the list follows
  // the URL rather than reading it once, and an answer for a query the user has already left is dropped.
  private readonly query$ = this.route.queryParamMap.pipe(
    map(toListQuery),
    distinctUntilChanged(
      (previous, next) =>
        sameArea(previous.area, next.area) &&
        previous.term === next.term &&
        previous.sort === next.sort &&
        previous.page === next.page,
    ),
  );

  protected readonly query = toSignal(this.query$, { requireSync: true });

  private readonly loading = reloadable(this.query$, (query) => this.load(query));

  protected readonly listing = toSignal(this.loading.state$, { requireSync: true });

  /** Asks again for what failed to load. */
  protected readonly reload = this.loading.reload;

  /**
   * What the list is of: the words searched for, the section or Default area the URL names, or "Library" for every
   * work and for a section whose name is not known.
   */
  protected readonly heading = computed(() => {
    const { term, area } = this.query();
    return term === null ? (this.sections.nameOf(area) ?? libraryName) : `Results for "${term}"`;
  });

  /** Opens "Add books" with the area being browsed preselected. */
  protected addBooks(): void {
    this.addBooksDialog.open([], areaParam(this.query().area));
  }

  /** Shows the first page in the chosen sort order. */
  protected sortBy(value: unknown): void {
    const sort = sortOptionsFor(this.query().term).find((option) => option.value === value)?.value;
    if (sort) {
      this.show({ sort, page: 1 });
    }
  }

  protected goToPage(page: number): void {
    this.show({ sort: this.query().sort, page });
  }

  protected pageCountOf(total: number): number {
    return Math.max(1, Math.ceil(total / pageSize));
  }

  protected totalText(total: number, searching: boolean): string {
    if (searching) {
      return total === 1 ? '1 work matches' : `${total} works match`;
    }
    return total === 1 ? '1 work' : `${total} works`;
  }

  /** Puts the sort and page in the URL, keeping its section and term, and leaves out each one that is at its default. */
  private show({ sort, page }: Pick<ListQuery, 'sort' | 'page'>): void {
    const defaultSort = defaultSortFor(this.query().term);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { sort: sort === defaultSort ? null : sort, page: page === 1 ? null : page },
      queryParamsHandling: 'merge',
    });
  }

  private async load({ area, term, sort, page }: ListQuery): Promise<Listing> {
    try {
      const response = await this.worksApi.list({
        SectionId: area.kind === 'section' ? area.id : undefined,
        NoSection: area.kind === 'default' ? true : undefined,
        Search: term ?? undefined,
        Sort: sort,
        Offset: (page - 1) * pageSize,
        Limit: pageSize,
      });
      return { status: 'loaded', page: response };
    } catch {
      return { status: 'failed' };
    }
  }
}

/**
 * Reads the list's query from the URL, taking the default for a sort or page it does not recognise, and for
 * relevance when no term is searched for.
 */
function toListQuery(params: ParamMap): ListQuery {
  const sortParam = params.get('sort');
  const pageParam = params.get('page');
  const term = readTerm(params);
  return {
    area: readArea(params),
    term,
    sort:
      sortOptionsFor(term).find((option) => option.value === sortParam)?.value ??
      defaultSortFor(term),
    page: pageParam !== null && /^[1-9][0-9]*$/.test(pageParam) ? Number(pageParam) : 1,
  };
}
