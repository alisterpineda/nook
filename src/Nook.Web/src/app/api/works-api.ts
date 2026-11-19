import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toParams } from './query-params';
import { components, paths } from './schema';

type ListParameters = NonNullable<paths['/api/works']['get']['parameters']['query']>;
// The schema allows a null sort for "none given", which a query leaves out instead.
type WorkListQuery = Omit<ListParameters, 'Sort'> & { Sort?: NonNullable<ListParameters['Sort']> };
type PageOfWorkSummaryResponse = components['schemas']['PageOfWorkSummaryResponse'];
type WorkDetailResponse = components['schemas']['WorkDetailResponse'];

/** The sizes the API resizes a cover to. The API's `CoverSize` holds the box each one fits inside. */
export type CoverSize = 'small' | 'large';

/**
 * Where a work's cover is at a size. The URL holds the cover hash, so a new cover gets a new URL and the browser can
 * keep each one for good.
 */
export function coverUrl(workId: string, coverHash: string, size: CoverSize): string {
  return `/api/works/${encodeURIComponent(workId)}/cover?size=${size}&v=${encodeURIComponent(coverHash)}`;
}

/** Where a part's file downloads from. */
export function partFileUrl(partId: string): string {
  return `/api/parts/${encodeURIComponent(partId)}/file`;
}

@Injectable({ providedIn: 'root' })
export class WorksApi {
  private readonly http = inject(HttpClient);

  /** One page of the works the signed-in user can see, from one section, from those in no section, or from all. */
  list(query: WorkListQuery): Promise<PageOfWorkSummaryResponse> {
    return firstValueFrom(
      this.http.get<PageOfWorkSummaryResponse>('/api/works', { params: toParams(query) }),
    );
  }

  /** One work with its contributors, tags, external ids and items. */
  get(id: string): Promise<WorkDetailResponse> {
    return firstValueFrom(
      this.http.get<WorkDetailResponse>(`/api/works/${encodeURIComponent(id)}`),
    );
  }
}
