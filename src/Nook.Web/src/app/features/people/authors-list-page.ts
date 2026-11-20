import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideUsers } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { distinctUntilChanged, map } from 'rxjs';
import { reloadable } from '../../core/reloadable';
import { PeopleApi } from '../../api/people-api';
import { components } from '../../api/schema';
import { LoadFailed } from '../../ui/load-failed';
import { LoadingRegion } from '../../ui/loading';
import { pageHeading } from '../../ui/page-heading';

type PageOfPersonSummaryResponse = components['schemas']['PageOfPersonSummaryResponse'];

/** Where the list stands for the page the URL names. */
type Listing =
  | { status: 'loading' }
  | { status: 'loaded'; page: PageOfPersonSummaryResponse }
  | { status: 'failed' };

const pageSize = 50;

/** One page of the people who wrote a work the user can see, each with how many of those works they wrote. */
@Component({
  selector: 'app-authors-list-page',
  imports: [
    LoadingRegion,
    NgIcon,
    RouterLink,
    HlmButtonImports,
    HlmEmptyImports,
    HlmSkeletonImports,
    LoadFailed,
  ],
  providers: [provideIcons({ lucideUsers })],
  template: `
    @let shownPage = page();
    <h1 class="mb-6" [class]="pageHeading">Authors</h1>
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
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideUsers" /></div>
            <div hlmEmptyTitle>No authors yet</div>
            <p hlmEmptyDescription>The authors of the books in the library will be listed here.</p>
          </div>
        </div>
      } @else {
        <ul class="flex flex-col gap-1" aria-label="Authors">
          @for (person of current.page.items; track person.id) {
            <li class="flex items-baseline gap-2 text-sm">
              <a class="font-medium hover:underline" [routerLink]="['/people', person.id]">{{
                person.name
              }}</a>
              <span class="text-muted-foreground">{{ workCountText(person.workCount) }}</span>
            </li>
          }
        </ul>
        @let pageCount = pageCountOf(current.page.total);
        <nav class="mt-8 flex flex-wrap items-center justify-between gap-2" aria-label="Pages">
          <p class="text-muted-foreground text-sm">{{ totalText(current.page.total) }}</p>
          <div class="flex items-center gap-2">
            <span class="text-muted-foreground text-sm"
              >Page {{ shownPage }} of {{ pageCount }}</span
            >
            <button
              hlmBtn
              variant="outline"
              size="sm"
              type="button"
              [disabled]="shownPage <= 1"
              (click)="goToPage(shownPage - 1)"
            >
              Previous
            </button>
            <button
              hlmBtn
              variant="outline"
              size="sm"
              type="button"
              [disabled]="shownPage >= pageCount"
              (click)="goToPage(shownPage + 1)"
            >
              Next
            </button>
          </div>
        </nav>
      }
    }
  `,
})
export class AuthorsListPage {
  protected readonly pageHeading = pageHeading;
  private readonly peopleApi = inject(PeopleApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  /** How wide, in rem, each line standing in for an author is while the list loads. */
  protected readonly skeletonWidths = [11, 14, 9, 13, 10, 15, 12, 9, 14, 11];

  // The pager changes the URL without recreating the page, so the list follows the URL rather than reading it once,
  // and an answer for a page the user has already left is dropped.
  private readonly page$ = this.route.queryParamMap.pipe(map(toPage), distinctUntilChanged());

  /** The page the URL names, counting from 1. */
  protected readonly page = toSignal(this.page$, { requireSync: true });

  private readonly loading = reloadable(this.page$, (query) => this.load(query));

  protected readonly listing = toSignal(this.loading.state$, { requireSync: true });

  /** Asks again for what failed to load. */
  protected readonly reload = this.loading.reload;

  /** Puts the page in the URL, leaving it out for the first. */
  protected goToPage(page: number): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { page: page === 1 ? null : page },
      queryParamsHandling: 'merge',
    });
  }

  protected pageCountOf(total: number): number {
    return Math.max(1, Math.ceil(total / pageSize));
  }

  protected totalText(total: number): string {
    return total === 1 ? '1 author' : `${total} authors`;
  }

  protected workCountText(count: number): string {
    return count === 1 ? '1 work' : `${count} works`;
  }

  private async load(page: number): Promise<Listing> {
    try {
      const response = await this.peopleApi.list({
        Role: 'author',
        Offset: (page - 1) * pageSize,
        Limit: pageSize,
      });
      return { status: 'loaded', page: response };
    } catch {
      return { status: 'failed' };
    }
  }
}

/** Reads the page from the URL, taking the first for a page that is not a whole number above 0. */
function toPage(params: ParamMap): number {
  const pageParam = params.get('page');
  return pageParam !== null && /^[1-9][0-9]*$/.test(pageParam) ? Number(pageParam) : 1;
}
