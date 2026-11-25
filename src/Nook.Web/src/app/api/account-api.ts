import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { components } from './schema';

type AccountResponse = components['schemas']['AccountResponse'];
type SetOwnPasswordRequest = components['schemas']['SetOwnPasswordRequest'];

@Injectable({ providedIn: 'root' })
export class AccountApi {
  private readonly http = inject(HttpClient);

  /** Who the signed-in user is and how they sign in. */
  get(): Promise<AccountResponse> {
    return firstValueFrom(this.http.get<AccountResponse>('/api/account'));
  }

  /** Changes the signed-in user's password, or adds one when they have none (then the current password is null). */
  setPassword(newPassword: string, currentPassword: string | null): Promise<void> {
    const body: SetOwnPasswordRequest = { currentPassword, newPassword };
    return firstValueFrom(this.http.put<void>('/api/account/password', body));
  }
}
