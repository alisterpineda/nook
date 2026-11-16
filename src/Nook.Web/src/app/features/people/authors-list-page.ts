import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { distinctUntilChanged, from, map, startWith, switchMap } from 'rxjs';
import { PeopleApi } from '../../api/people-api';
import { components } from '../../api/schema';
import { somethingWentWrong } from '../../core/messages';

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
  imports: [HlmButtonImports, RouterLink],
  template: `
    @let shownPage = page();
    <h1 class="mb-4 text-lg font-semibold">Authors</h1>
    @let current = listing();
    @if (current.status === 'failed') {
      <p class="text-destructive text-sm" role="alert">{{ somethingWentWrong }}</p>
    } @else if (current.status === 'loaded') {
      @if (current.page.total === 0) {
        <p class="text-muted-foreground text-sm">No authors yet</p>
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
        <nav class="mt-4 flex flex-wrap items-center justify-between gap-2" aria-label="Pages">
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
  private readonly peopleApi = inject(PeopleApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly somethingWentWrong = somethingWentWrong;

  // The pager changes the URL without recreating the page, so the list follows the URL rather than reading it once,
  // and an answer for a page the user has already left is dropped.
  private readonly page$ = this.route.queryParamMap.pipe(map(toPage), distinctUntilChanged());

  /** The page the URL names, counting from 1. */
  protected readonly page = toSignal(this.page$, { requireSync: true });

  protected readonly listing = toSignal(
    this.page$.pipe(
      switchMap((page) => from(this.load(page)).pipe(startWith<Listing>({ status: 'loading' }))),
    ),
    { requireSync: true },
  );

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
