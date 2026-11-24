import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toParams } from './query-params';
import { components, paths } from './schema';

type SearchParameters = NonNullable<paths['/api/search']['get']['parameters']['query']>;
// The schema marks the term optional, but the API refuses a search without one.
type SearchQuery = SearchParameters & { Q: string };

export type SearchResponse = components['schemas']['SearchResponse'];
export type SearchGroup = components['schemas']['SearchGroup'];
export type SearchHit = components['schemas']['SearchHit'];
export type SearchSource = components['schemas']['SearchSource'];

@Injectable({ providedIn: 'root' })
export class SearchApi {
  private readonly http = inject(HttpClient);

  /** The best matches for a term among what the caller can see, grouped by the kind of thing found. */
  search(query: SearchQuery): Promise<SearchResponse> {
    return firstValueFrom(
      this.http.get<SearchResponse>('/api/search', { params: toParams(query) }),
    );
  }
}
