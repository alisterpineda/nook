import { HttpTestingController } from '@angular/common/http/testing';
import { components } from '../api/schema';

type UploadActivityResponse = components['schemas']['UploadActivityResponse'];

/** An EPUB file of `size` bytes, for specs. */
export function epub(name: string, size = 10): File {
  return new File(['x'.repeat(size)], name, { type: 'application/epub+zip' });
}

/** The server's row for the `n`th upload, named `book-<n>.epub`, for specs. */
export function uploadActivity(
  n: number,
  status: UploadActivityResponse['status'] = 'queued',
  extra: Partial<UploadActivityResponse> = {},
): UploadActivityResponse {
  return {
    jobId: `0198a1b2-0000-7000-8000-${String(n).padStart(12, '0')}`,
    fileName: `book-${n}.epub`,
    sectionId: null,
    sectionName: null,
    submittedAt: '2026-11-20T10:00:00Z',
    status,
    workId: null,
    failure: null,
    ...extra,
  };
}

/** Answers every open request for the user's uploads with none, for specs that are not about them. */
export function flushUploadActivity(httpTesting: HttpTestingController): void {
  for (const request of httpTesting.match('/api/activity/uploads')) {
    request.flush([]);
  }
}
