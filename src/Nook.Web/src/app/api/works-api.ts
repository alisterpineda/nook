import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { components, paths } from './schema';

type WorkListQuery = NonNullable<paths['/api/works']['get']['parameters']['query']>;
type PageOfWorkSummaryResponse = components['schemas']['PageOfWorkSummaryResponse'];
type WorkDetailResponse = components['schemas']['WorkDetailResponse'];

@Injectable({ providedIn: 'root' })
export class WorksApi {
  private readonly http = inject(HttpClient);

  /** One page of the works the signed-in user can see, from one library or from all of them. */
  list(query: WorkListQuery): Promise<PageOfWorkSummaryResponse> {
    let params = new HttpParams();
    for (const [name, value] of Object.entries(query)) {
      if (value !== undefined) {
        params = params.set(name, value);
      }
    }
    return firstValueFrom(this.http.get<PageOfWorkSummaryResponse>('/api/works', { params }));
  }

  /** One work with its contributors, tags, external ids and items. */
  get(id: string): Promise<WorkDetailResponse> {
    return firstValueFrom(
      this.http.get<WorkDetailResponse>(`/api/works/${encodeURIComponent(id)}`),
    );
  }
}
