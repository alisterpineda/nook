import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { components } from './schema';

type VersionResponse = components['schemas']['VersionResponse'];

@Injectable({ providedIn: 'root' })
export class VersionApi {
  private readonly http = inject(HttpClient);

  /** The version of Nook the API is running. Needs a signed-in user. */
  get(): Promise<VersionResponse> {
    return firstValueFrom(this.http.get<VersionResponse>('/api/version'));
  }
}
