import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toParams } from './query-params';
import { components, paths } from './schema';

type UserListQuery = NonNullable<paths['/api/users']['get']['parameters']['query']>;
type PageOfUserSummaryResponse = components['schemas']['PageOfUserSummaryResponse'];
type UserSummaryResponse = components['schemas']['UserSummaryResponse'];
type AddUserRequest = components['schemas']['AddUserRequest'];
type SetUserPasswordRequest = components['schemas']['SetUserPasswordRequest'];

@Injectable({ providedIn: 'root' })
export class UsersApi {
  private readonly http = inject(HttpClient);

  /** One page of the people who have an account, in the order the query names. */
  list(query: UserListQuery): Promise<PageOfUserSummaryResponse> {
    return firstValueFrom(
      this.http.get<PageOfUserSummaryResponse>('/api/users', { params: toParams(query) }),
    );
  }

  /** Creates an account with a password the admin chose. */
  add(request: AddUserRequest): Promise<UserSummaryResponse> {
    return firstValueFrom(this.http.post<UserSummaryResponse>('/api/users', request));
  }

  /** Gives another user a password the admin chose, ending that user's sessions. */
  async setPassword(id: string, request: SetUserPasswordRequest): Promise<void> {
    await firstValueFrom(this.http.put<void>(`/api/users/${id}/password`, request));
  }
}
