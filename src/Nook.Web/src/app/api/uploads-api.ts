import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { components } from './schema';

type UploadResponse = components['schemas']['UploadResponse'];

@Injectable({ providedIn: 'root' })
export class UploadsApi {
  private readonly http = inject(HttpClient);

  /**
   * Sends one file to a section, or to the Default area when `sectionId` is null, and answers with the id of the job
   * that ingests it. Unlike the other clients this returns an observable, so whoever sent the file can cancel the
   * request by unsubscribing.
   */
  upload(sectionId: string | null, file: File): Observable<UploadResponse> {
    const form = new FormData();
    if (sectionId !== null) {
      form.append('sectionId', sectionId);
    }
    form.append('file', file);
    return this.http.post<UploadResponse>('/api/uploads', form);
  }
}
