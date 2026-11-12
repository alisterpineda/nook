import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { components } from './schema';

type LibraryResponse = components['schemas']['LibraryResponse'];

@Injectable({ providedIn: 'root' })
export class LibrariesApi {
  private readonly http = inject(HttpClient);

  /** The libraries the signed-in user can see, ordered by name. */
  list(): Promise<LibraryResponse[]> {
    return firstValueFrom(this.http.get<LibraryResponse[]>('/api/libraries'));
  }
}
