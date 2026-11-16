import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmToggleGroupImports } from '@spartan-ng/helm/toggle-group';
import { distinctUntilChanged, from, map, startWith, switchMap } from 'rxjs';
import { components } from '../../api/schema';
import { WorksApi } from '../../api/works-api';
import { LibraryArea, readArea, sameArea } from '../../core/default-area';
import { somethingWentWrong } from '../../core/messages';
import { WorkCover } from './work-cover';

type PageOfWorkSummaryResponse = components['schemas']['PageOfWorkSummaryResponse'];
type WorkSort = components['schemas']['WorkSort'];

/** What the URL asks the list to show. */
interface ListQuery {
  area: LibraryArea;
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

const defaultSort: WorkSort = 'title';

/** The sort orders the list offers, in the order the control shows them. */
const sortOptions: readonly { value: WorkSort; label: string }[] = [
  { value: 'title', label: 'Title' },
  { value: 'added', label: 'Recently added' },
];

/**
 * One page of the works in the section the URL names, of the works in no section when it names the Default area,
 * or of every work the user can see when it names none, in the sort order and at the page the URL names.
 */
@Component({
  selector: 'app-works-list-page',
  imports: [RouterLink, HlmButtonImports, HlmToggleGroupImports, WorkCover],
  template: `
    @let shownQuery = query();
    <div class="mb-4 flex flex-wrap items-center justify-between gap-2">
      <h1 class="text-lg font-semibold">Library</h1>
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
        @for (option of sortOptions; track option.value) {
          <button hlmToggleGroupItem type="button" [value]="option.value">
            {{ option.label }}
          </button>
        }
      </div>
    </div>
    @let current = listing();
    @if (current.status === 'failed') {
      <p class="text-destructive text-sm" role="alert">{{ somethingWentWrong }}</p>
    } @else if (current.status === 'loaded') {
      @if (current.page.total === 0) {
        <p class="text-muted-foreground text-sm">No works yet</p>
      } @else {
        <ul
          class="grid grid-cols-[repeat(auto-fill,minmax(8rem,1fr))] gap-x-4 gap-y-6"
          aria-label="Works"
        >
          @for (work of current.page.items; track work.id) {
            <li>
              <a class="group flex flex-col gap-1" [routerLink]="['/works', work.id]">
                <app-work-cover class="mb-1" [work]="work" size="small" />
                <span class="line-clamp-2 text-sm font-medium group-hover:underline">{{
                  work.title
                }}</span>
                <span class="text-muted-foreground line-clamp-2 text-xs">{{
                  work.authors.join(', ')
                }}</span>
              </a>
            </li>
          }
        </ul>
        @let pageCount = pageCountOf(current.page.total);
        <nav class="mt-4 flex flex-wrap items-center justify-between gap-2" aria-label="Pages">
          <p class="text-muted-foreground text-sm">{{ totalText(current.page.total) }}</p>
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
  private readonly worksApi = inject(WorksApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly somethingWentWrong = somethingWentWrong;
  protected readonly sortOptions = sortOptions;

  // The sidebar, the sort control and the pager all change the URL without recreating the page, so the list follows
  // the URL rather than reading it once, and an answer for a query the user has already left is dropped.
  private readonly query$ = this.route.queryParamMap.pipe(
    map(toListQuery),
    distinctUntilChanged(
      (previous, next) =>
        sameArea(previous.area, next.area) &&
        previous.sort === next.sort &&
        previous.page === next.page,
    ),
  );

  protected readonly query = toSignal(this.query$, { requireSync: true });

  protected readonly listing = toSignal(
    this.query$.pipe(
      switchMap((query) => from(this.load(query)).pipe(startWith<Listing>({ status: 'loading' }))),
    ),
    { requireSync: true },
  );

  /** Shows the first page in the chosen sort order. */
  protected sortBy(value: unknown): void {
    const sort = sortOptions.find((option) => option.value === value)?.value;
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

  protected totalText(total: number): string {
    return total === 1 ? '1 work' : `${total} works`;
  }

  /** Puts the sort and page in the URL, keeping its section, and leaves out each one that is at its default. */
  private show({ sort, page }: Omit<ListQuery, 'area'>): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { sort: sort === defaultSort ? null : sort, page: page === 1 ? null : page },
      queryParamsHandling: 'merge',
    });
  }

  private async load({ area, sort, page }: ListQuery): Promise<Listing> {
    try {
      const response = await this.worksApi.list({
        SectionId: area.kind === 'section' ? area.id : undefined,
        NoSection: area.kind === 'default' ? true : undefined,
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

/** Reads the list's query from the URL, taking the default for a sort or page it does not recognise. */
function toListQuery(params: ParamMap): ListQuery {
  const sortParam = params.get('sort');
  const pageParam = params.get('page');
  return {
    area: readArea(params),
    sort: sortOptions.find((option) => option.value === sortParam)?.value ?? defaultSort,
    page: pageParam !== null && /^[1-9][0-9]*$/.test(pageParam) ? Number(pageParam) : 1,
  };
}
