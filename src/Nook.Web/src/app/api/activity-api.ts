import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { components } from './schema';

type UploadActivityResponse = components['schemas']['UploadActivityResponse'];

@Injectable({ providedIn: 'root' })
export class ActivityApi {
  private readonly http = inject(HttpClient);

  /** The user's most recent uploads, newest first. */
  uploads(): Promise<UploadActivityResponse[]> {
    return firstValueFrom(this.http.get<UploadActivityResponse[]>('/api/activity/uploads'));
  }

  /** Runs a failed upload again. */
  async retry(jobId: string): Promise<void> {
    await firstValueFrom(
      this.http.post<void>(`/api/activity/uploads/${encodeURIComponent(jobId)}/retry`, null),
    );
  }

  /** Removes a failed upload, along with the file it staged. */
  async remove(jobId: string): Promise<void> {
    await firstValueFrom(
      this.http.delete<void>(`/api/activity/uploads/${encodeURIComponent(jobId)}`),
    );
  }
}
