import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toParams } from './query-params';
import { components, paths } from './schema';

type UserListQuery = NonNullable<paths['/api/users']['get']['parameters']['query']>;
type PageOfUserSummaryResponse = components['schemas']['PageOfUserSummaryResponse'];

@Injectable({ providedIn: 'root' })
export class UsersApi {
  private readonly http = inject(HttpClient);

  /** One page of the people who have an account, in the order the query names. */
  list(query: UserListQuery): Promise<PageOfUserSummaryResponse> {
    return firstValueFrom(
      this.http.get<PageOfUserSummaryResponse>('/api/users', { params: toParams(query) }),
    );
  }
}
