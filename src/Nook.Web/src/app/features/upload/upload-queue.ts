import { HttpErrorResponse } from '@angular/common/http';
import { DOCUMENT, DestroyRef, Injectable, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { components } from '../../api/schema';
import { UploadsApi } from '../../api/uploads-api';
import { somethingWentWrong } from '../../core/messages';

type ProblemDetails = components['schemas']['ProblemDetails'];

/**
 * Where a file in this tab stands: `waiting` for its turn, `uploading` while its request is open, or `failed` when
 * the server refused it. Once the server takes a file it leaves the queue, and its upload is followed in the
 * Notifications list.
 */
export type UploadStatus = 'waiting' | 'uploading' | 'failed';

/** One file added to the queue. */
export interface UploadRow {
  readonly id: number;
  readonly name: string;
  readonly file: File;
  /** The section selected when the file was added, which is where it goes, or `null` for the Default area. */
  readonly sectionId: string | null;
  readonly status: UploadStatus;
  /** How much of the file has been sent, from 0 to 100, while it is `uploading`. */
  readonly percent?: number;
  /** Why a `failed` row failed: the server's reason for refusing the file. */
  readonly error?: string;
}

/**
 * The files in this tab not yet taken by the server, sent one request at a time in the order they were added. It is
 * provided in the root so uploads keep going while the user moves between pages.
 */
@Injectable({ providedIn: 'root' })
export class UploadQueue {
  private readonly uploads = inject(UploadsApi);
  private readonly allRows = signal<readonly UploadRow[]>([]);
  private nextId = 1;
  /** The upload whose request is open, if any. */
  private sending: Subscription | null = null;

  /** Every file still in the queue, oldest first. */
  readonly rows = this.allRows.asReadonly();

  constructor() {
    // Closing or reloading the tab loses the files not yet sent, so the browser asks first. Both calls are made
    // because browsers have asked for the prompt in either way.
    const window = inject(DOCUMENT).defaultView;
    const questionLeaving = (event: BeforeUnloadEvent) => {
      if (this.allRows().some((row) => row.status === 'waiting' || row.status === 'uploading')) {
        event.preventDefault();
        event.returnValue = true;
      }
    };
    window?.addEventListener('beforeunload', questionLeaving);

    inject(DestroyRef).onDestroy(() => {
      window?.removeEventListener('beforeunload', questionLeaving);
      this.sending?.unsubscribe();
      this.sending = null;
    });
  }

  /**
   * Adds a row per file, each to go to `sectionId` or, when it is `null`, the Default area, and sends them after any
   * already waiting.
   */
  add(files: Iterable<File>, sectionId: string | null): void {
    const added = Array.from(files, (file): UploadRow => ({
      id: this.nextId++,
      name: file.name,
      file,
      sectionId,
      status: 'waiting',
    }));
    this.allRows.update((rows) => [...rows, ...added]);
    this.sendNext();
  }

  /** Sends a refused file again, after any already waiting. A row that has not failed is left alone. */
  retry(id: number): void {
    if (this.allRows().find((row) => row.id === id)?.status !== 'failed') {
      return;
    }
    this.change(id, { status: 'waiting', error: undefined, percent: undefined });
    this.sendNext();
  }

  /** Takes a refused file away. A row that has not failed is left alone. */
  remove(id: number): void {
    this.allRows.update((rows) => rows.filter((row) => row.id !== id || row.status !== 'failed'));
  }

  /** Drops every row and cancels the open upload, so nothing carries over to the next user. */
  reset(): void {
    this.allRows.set([]);
    this.sending?.unsubscribe();
    this.sending = null;
  }

  private sendNext(): void {
    if (this.sending) {
      return;
    }
    const row = this.allRows().find((candidate) => candidate.status === 'waiting');
    if (!row) {
      return;
    }
    this.change(row.id, { status: 'uploading', percent: 0 });

    // Held before subscribing, so a request that ends at once still finds itself the one sending.
    const sending = new Subscription();
    this.sending = sending;
    const done = () => {
      if (this.sending === sending) {
        this.sending = null;
        this.sendNext();
      }
    };
    sending.add(
      this.uploads.upload(row.sectionId, row.file).subscribe({
        next: (event) => {
          if (event.kind === 'progress') {
            this.change(row.id, { percent: event.percent });
          } else {
            // The server has the file: its upload shows in the Notifications list from here.
            this.allRows.update((rows) => rows.filter((candidate) => candidate.id !== row.id));
          }
        },
        error: (error: unknown) => {
          this.change(row.id, {
            status: 'failed',
            error: refusalMessage(error),
            percent: undefined,
          });
          done();
        },
        complete: done,
      }),
    );
  }

  private change(id: number, change: Partial<UploadRow>): void {
    this.allRows.update((rows) => rows.map((row) => (row.id === id ? { ...row, ...change } : row)));
  }
}

/** The server's words for refusing a file, or fixed words when it gave none. */
function refusalMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse && typeof error.error === 'object' && error.error) {
    const problem = error.error as ProblemDetails;
    return problem.detail || problem.title || somethingWentWrong;
  }
  return somethingWentWrong;
}
