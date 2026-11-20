import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { components } from './schema';

type JobDetailResponse = components['schemas']['JobDetailResponse'];

@Injectable({ providedIn: 'root' })
export class JobsApi {
  private readonly http = inject(HttpClient);

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
