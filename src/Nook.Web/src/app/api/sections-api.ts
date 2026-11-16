import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { components } from './schema';

type SectionResponse = components['schemas']['SectionResponse'];

@Injectable({ providedIn: 'root' })
export class SectionsApi {
  private readonly http = inject(HttpClient);

  /** The sections the signed-in user can see, ordered by name. */
  list(): Promise<SectionResponse[]> {
    return firstValueFrom(this.http.get<SectionResponse[]>('/api/sections'));
  }
}
