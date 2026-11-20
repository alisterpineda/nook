import { HttpClient, HttpEventType } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, filter, firstValueFrom, map } from 'rxjs';
import { components } from './schema';

type UploadResponse = components['schemas']['UploadResponse'];
type UploadLimitsResponse = components['schemas']['UploadLimitsResponse'];

/** What an upload reports: how much of the file has been sent, then the job the server started for it. */
export type UploadEvent =
  | { readonly kind: 'progress'; readonly percent: number }
  | { readonly kind: 'accepted'; readonly jobId: string };

@Injectable({ providedIn: 'root' })
export class UploadsApi {
  private readonly http = inject(HttpClient);

  /**
   * Sends one file to a section, or to the Default area when `sectionId` is null. It reports the percentage sent as
   * the file goes, then the id of the job that ingests it. Unlike the other clients this returns an observable, so whoever sent the file can cancel the
   * request by unsubscribing.
   */
  upload(sectionId: string | null, file: File): Observable<UploadEvent> {
    const form = new FormData();
    if (sectionId !== null) {
      form.append('sectionId', sectionId);
    }
    form.append('file', file);
    return this.http
      .post<UploadResponse>('/api/uploads', form, { reportProgress: true, observe: 'events' })
      .pipe(
        filter(
          (event) =>
            event.type === HttpEventType.UploadProgress || event.type === HttpEventType.Response,
        ),
        map((event): UploadEvent => {
          if (event.type === HttpEventType.Response) {
            return { kind: 'accepted', jobId: event.body!.jobId };
          }
          const percent = event.total ? Math.floor((event.loaded / event.total) * 100) : 0;
          return { kind: 'progress', percent };
        }),
      );
  }

  /** The largest file the server takes. */
  limits(): Promise<UploadLimitsResponse> {
    return firstValueFrom(this.http.get<UploadLimitsResponse>('/api/uploads/limits'));
  }
}
