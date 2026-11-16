import { HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { distinctUntilChanged, from, map, startWith, switchMap } from 'rxjs';
import { PeopleApi } from '../../api/people-api';
import { components } from '../../api/schema';
import { somethingWentWrong } from '../../core/messages';
import { WorkCard } from '../works/work-card';
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

/**
 * One person with every work they are credited on, by role in the API's order, and within a role by series with the
 * works in no series after them.
 */
@Component({
  selector: 'app-person-page',
  imports: [WorkCard],
  template: `
    @let current = showing();
    @if (current.status === 'failed') {
      <p class="text-destructive text-sm" role="alert">{{ somethingWentWrong }}</p>
    } @else if (current.status === 'not-found') {
      <h1 class="text-lg font-semibold">Person not found</h1>
    } @else if (current.status === 'loaded') {
      <h1 class="mb-4 text-lg font-semibold">{{ current.person.name }}</h1>
      <div class="flex flex-col gap-8">
        @for (group of current.roles; track group.role) {
          <section class="flex flex-col gap-4">
            @if (current.roles.length > 1) {
              <h2 class="text-base font-semibold">{{ roleLabel(group.role) }}</h2>
            }
            @for (run of group.runs; track $index) {
              <div class="flex flex-col gap-2">
                @if (run.heading !== null) {
                  <h3 class="text-sm font-medium">{{ run.heading }}</h3>
                }
                <ul
                  class="grid grid-cols-[repeat(auto-fill,minmax(8rem,1fr))] gap-x-4 gap-y-6"
                  aria-label="Works"
                >
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
  private readonly peopleApi = inject(PeopleApi);
  private readonly route = inject(ActivatedRoute);

  protected readonly somethingWentWrong = somethingWentWrong;
  protected readonly roleLabel = roleLabel;

  // Going from one person to another keeps this page, so it follows the id in the URL rather than reading it once,
  // and an answer for a person the user has already left is dropped.
  protected readonly showing = toSignal(
    this.route.paramMap.pipe(
      map((params) => params.get('id') ?? ''),
      distinctUntilChanged(),
      switchMap((id) => from(this.load(id)).pipe(startWith<Showing>({ status: 'loading' }))),
    ),
    { requireSync: true },
  );

  private async load(id: string): Promise<Showing> {
    try {
      const person = await this.peopleApi.get(id);
      return {
        status: 'loaded',
        person,
        roles: person.roles.map(({ role, works }) => ({ role, runs: runsOf(works) })),
      };
    } catch (error) {
      return error instanceof HttpErrorResponse && error.status === HttpStatusCode.NotFound
        ? { status: 'not-found' }
        : { status: 'failed' };
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
