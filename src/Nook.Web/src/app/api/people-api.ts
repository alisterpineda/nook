import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toParams } from './query-params';
import { components, paths } from './schema';

type PersonListQuery = NonNullable<paths['/api/people']['get']['parameters']['query']>;
type PageOfPersonSummaryResponse = components['schemas']['PageOfPersonSummaryResponse'];
type PersonDetailResponse = components['schemas']['PersonDetailResponse'];

@Injectable({ providedIn: 'root' })
export class PeopleApi {
  private readonly http = inject(HttpClient);

  /** One page of the people credited on a work the signed-in user can see, optionally only those in one role. */
  list(query: PersonListQuery): Promise<PageOfPersonSummaryResponse> {
    return firstValueFrom(
      this.http.get<PageOfPersonSummaryResponse>('/api/people', { params: toParams(query) }),
    );
  }

  /** One person with the works they are credited on, grouped by role. */
  get(id: string): Promise<PersonDetailResponse> {
    return firstValueFrom(
      this.http.get<PersonDetailResponse>(`/api/people/${encodeURIComponent(id)}`),
    );
  }
}
