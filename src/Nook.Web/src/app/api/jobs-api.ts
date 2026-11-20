import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toParams } from './query-params';
import { components, paths } from './schema';

type JobListQuery = NonNullable<paths['/api/jobs']['get']['parameters']['query']>;
type PageOfJobSummaryResponse = components['schemas']['PageOfJobSummaryResponse'];
type JobDetailResponse = components['schemas']['JobDetailResponse'];

@Injectable({ providedIn: 'root' })
export class JobsApi {
  private readonly http = inject(HttpClient);

  /** One page of jobs, newest first, in the statuses asked for or, when none is, in every status. */
  list(query: JobListQuery): Promise<PageOfJobSummaryResponse> {
    return firstValueFrom(
      this.http.get<PageOfJobSummaryResponse>('/api/jobs', { params: toParams(query) }),
    );
  }

  /** One job with its steps, children and, once it has succeeded, its result. */
  get(id: string): Promise<JobDetailResponse> {
    return firstValueFrom(this.http.get<JobDetailResponse>(`/api/jobs/${encodeURIComponent(id)}`));
  }

  /** Runs a failed job again. */
  async retry(id: string): Promise<void> {
    await firstValueFrom(this.http.post<void>(`/api/jobs/${encodeURIComponent(id)}/retry`, null));
  }

  /** Deletes a job that is not running, along with the file it staged. */
  async delete(id: string): Promise<void> {
    await firstValueFrom(this.http.delete<void>(`/api/jobs/${encodeURIComponent(id)}`));
  }
}
