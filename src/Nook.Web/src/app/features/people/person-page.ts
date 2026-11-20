import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { distinctUntilChanged, map } from 'rxjs';
import { failureOf, reloadable } from '../../core/reloadable';
import { PeopleApi } from '../../api/people-api';
import { components } from '../../api/schema';
import { PageName } from '../../core/page-name';
import { LoadFailed } from '../../ui/load-failed';
import { pageHeading } from '../../ui/page-heading';
import { NotFound } from '../../ui/not-found';
import { WorkCard } from '../works/work-card';
import { workGrid } from '../works/work-grid';
import { WorkGridSkeleton } from '../works/work-grid-skeleton';
import { roleLabel } from '../works/work-labels';

type ContributorRole = components['schemas']['ContributorRole'];
type PersonDetailResponse = components['schemas']['PersonDetailResponse'];
type WorkSummaryResponse = components['schemas']['WorkSummaryResponse'];

/** Works that stand together under one heading, or under none. */
interface Run {
  heading: string | null;
  works: WorkSummaryResponse[];
}

/** The works a person holds one role on, cut into runs. */
interface RoleGroup {
  role: ContributorRole;
  runs: Run[];
}

/** Where the page stands for the person the URL names. */
type Showing =
  | { status: 'loading' }
  | { status: 'loaded'; person: PersonDetailResponse; roles: RoleGroup[] }
  | { status: 'not-found' }
  | { status: 'failed' };

/** What the page says, and the header with it, when there is no such person. */
const notFoundHeading = 'Person not found';

/**
 * One person with every work they are credited on, by role in the API's order, and within a role by series with the
 * works in no series after them.
 */
@Component({
  selector: 'app-person-page',
  imports: [HlmSkeletonImports, LoadFailed, NotFound, WorkCard, WorkGridSkeleton],
  template: `
    @let current = showing();
    @if (current.status === 'loading') {
      <div hlmSkeleton class="mb-6 h-8 w-56"></div>
      <app-work-grid-skeleton [count]="6" />
    } @else if (current.status === 'failed') {
      <app-load-failed (retry)="reload()" />
    } @else if (current.status === 'not-found') {
      <app-not-found [heading]="notFoundHeading" backLink="/authors" backLabel="Back to authors" />
    } @else {
      <h1 class="mb-6" [class]="pageHeading">{{ current.person.name }}</h1>
      <div class="flex flex-col gap-8">
        @for (group of current.roles; track group.role) {
          <section class="flex flex-col gap-4">
            @if (current.roles.length > 1) {
              <h2 class="text-base font-semibold">{{ roleLabel(group.role) }}</h2>
            }
            @for (run of group.runs; track $index) {
              <div class="flex flex-col gap-2">
                @if (run.heading !== null) {
                  <h3 class="text-muted-foreground text-sm font-medium">{{ run.heading }}</h3>
                }
                <ul [class]="workGrid" aria-label="Works">
                  @for (work of run.works; track work.id) {
                    <li>
                      <app-work-card [work]="work" />
                    </li>
                  }
                </ul>
              </div>
            }
          </section>
        }
      </div>
    }
  `,
})
export class PersonPage {
  protected readonly notFoundHeading = notFoundHeading;
  protected readonly pageHeading = pageHeading;
  private readonly peopleApi = inject(PeopleApi);
  private readonly route = inject(ActivatedRoute);

  private readonly pageName = inject(PageName);

  protected readonly roleLabel = roleLabel;
  protected readonly workGrid = workGrid;

  // Going from one person to another keeps this page, so it follows the id in the URL rather than reading it once,
  // and an answer for a person the user has already left is dropped.
  private readonly loading = reloadable(
    this.route.paramMap.pipe(
      map((params) => params.get('id') ?? ''),
      distinctUntilChanged(),
    ),
    (id) => this.load(id),
  );

  protected readonly showing = toSignal(this.loading.state$, { requireSync: true });

  /** Asks again for what failed to load. */
  protected readonly reload = this.loading.reload;

  private async load(id: string): Promise<Showing> {
    try {
      const person = await this.peopleApi.get(id);
      this.pageName.set(id, person.name);
      return {
        status: 'loaded',
        person,
        roles: person.roles.map(({ role, works }) => ({ role, runs: runsOf(works) })),
      };
    } catch (error) {
      const status = failureOf(error);
      // The header waits for a name, so it is told what the page shows in place of one.
      this.pageName.set(id, status === 'not-found' ? notFoundHeading : 'Person');
      return { status };
    }
  }
}

/**
 * Cuts one role's works into runs in the API's order: a run under the series' name each time the series changes, so
 * two series of one name stay apart, then the works in no series, under "Other works" only when a series run is above
 * them.
 */
function runsOf(works: WorkSummaryResponse[]): Run[] {
  const runs: Run[] = [];
  const others: WorkSummaryResponse[] = [];
  let seriesId: string | null = null;
  for (const work of works) {
    if (!work.series) {
      others.push(work);
    } else if (work.series.id === seriesId) {
      runs[runs.length - 1].works.push(work);
    } else {
      seriesId = work.series.id;
      runs.push({ heading: work.series.name, works: [work] });
    }
  }
  if (others.length > 0) {
    runs.push({ heading: runs.length > 0 ? 'Other works' : null, works: others });
  }
  return runs;
}
