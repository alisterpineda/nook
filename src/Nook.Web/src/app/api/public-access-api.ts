import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { components } from './schema';

type PublicAccessRequest = components['schemas']['PublicAccessRequest'];
type PublicAccessResponse = components['schemas']['PublicAccessResponse'];

@Injectable({ providedIn: 'root' })
export class PublicAccessApi {
  private readonly http = inject(HttpClient);

  /** What a visitor with no account may do: the switch, the Anonymous levels, and every section with its level. */
  get(): Promise<PublicAccessResponse> {
    return firstValueFrom(this.http.get<PublicAccessResponse>('/api/public-access'));
  }

  /** Replaces all of the settings. A section left out, or given `none`, is closed. */
  async save(settings: PublicAccessRequest): Promise<void> {
    await firstValueFrom(this.http.put('/api/public-access', settings));
  }
}
