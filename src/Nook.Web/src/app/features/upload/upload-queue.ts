import { HttpErrorResponse } from '@angular/common/http';
import { DOCUMENT, DestroyRef, Injectable, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { IngestResult, JobsApi } from '../../api/jobs-api';
import { components } from '../../api/schema';
import { UploadsApi } from '../../api/uploads-api';
import { somethingWentWrong } from '../../core/messages';

type JobDetailResponse = components['schemas']['JobDetailResponse'];
type ProblemDetails = components['schemas']['ProblemDetails'];

/**
 * Where a file stands: `waiting` for its turn, `uploading` while its request is open, `ingesting` once the server
 * has taken it and started a job, `added` or `duplicate` once the job has made it a new item or matched
 * an item already held, or `failed` when the server refused it or its job failed.
 */
export type UploadStatus = 'waiting' | 'uploading' | 'ingesting' | 'added' | 'duplicate' | 'failed';

/** One file added to the queue. */
export interface UploadRow {
  readonly id: number;
  readonly name: string;
  readonly file: File;
  /** The section selected when the file was added, which is where it goes, or `null` for the Default area. */
  readonly sectionId: string | null;
  readonly status: UploadStatus;
  /** The job ingesting the file, once the server has answered. */
  readonly jobId?: string;
  /** Why a `failed` row failed. */
  readonly error?: string;
  /** The work an `added` or `duplicate` file went into. */
  readonly workId?: string;
}

/**
 * The files being added, sent one request at a time in the order they were added. It is provided in
 * the root so uploads keep going while the user moves between pages.
 */
@Injectable({ providedIn: 'root' })
export class UploadQueue {
  private readonly uploads = inject(UploadsApi);
  private readonly jobs = inject(JobsApi);
  private readonly allRows = signal<readonly UploadRow[]>([]);
  private nextId = 1;
  /** The upload whose request is open, if any. */
  private sending: Subscription | null = null;
  /** The timer for the next round of polls, if one is due. */
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  /** Whether a round of polls is waiting on its answers. */
  private polling = false;
  /** Changes whenever polling is stopped, so a round that was under way then does not start another. */
  private pollGeneration = 0;

  /** Every file added, oldest first. */
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
      this.stopPolling();
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

  /**
   * Tries a failed row again: runs its job again when it has one, otherwise sends its file again. A job that is no
   * longer failed is followed again, to show where it stands.
   */
  async retry(row: UploadRow): Promise<void> {
    if (!this.isFailed(row.id)) {
      return;
    }
    if (!row.jobId) {
      this.change(row.id, { status: 'waiting', error: undefined });
      this.sendNext();
      return;
    }
    try {
      await this.jobs.retry(row.jobId);
    } catch (error) {
      if (!(error instanceof HttpErrorResponse && error.status === 409)) {
        this.changeFailed(row.id, { error: somethingWentWrong });
        return;
      }
    }
    if (this.changeFailed(row.id, { status: 'ingesting', error: undefined })) {
      this.schedulePoll();
    }
  }

  /** Takes a failed row away, deleting its job and the file it staged when it has one. */
  async remove(row: UploadRow): Promise<void> {
    if (!this.isFailed(row.id)) {
      return;
    }
    if (row.jobId) {
      try {
        await this.jobs.delete(row.jobId);
      } catch (error) {
        if (!(error instanceof HttpErrorResponse && error.status === 404)) {
          this.changeFailed(row.id, { error: somethingWentWrong });
          return;
        }
      }
    }
    this.allRows.update((rows) => rows.filter((candidate) => candidate.id !== row.id));
  }

  /** Takes away the rows that were added or found to be duplicates. */
  clearFinished(): void {
    this.allRows.update((rows) => rows.filter((row) => !isFinished(row)));
  }

  /** Drops every row, cancels the open upload and stops following jobs, so nothing carries over to the next user. */
  reset(): void {
    this.allRows.set([]);
    this.sending?.unsubscribe();
    this.sending = null;
    this.stopPolling();
  }

  private sendNext(): void {
    if (this.sending) {
      return;
    }
    const row = this.allRows().find((candidate) => candidate.status === 'waiting');
    if (!row) {
      return;
    }
    this.change(row.id, { status: 'uploading' });

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
        next: ({ jobId }) => {
          this.change(row.id, { status: 'ingesting', jobId });
          this.schedulePoll();
        },
        error: (error: unknown) => {
          this.change(row.id, { status: 'failed', error: refusalMessage(error) });
          done();
        },
        complete: done,
      }),
    );
  }

  /** Starts the next round of polls in a second, unless one is already due or under way, or nothing is ingesting. */
  private schedulePoll(): void {
    if (
      this.pollTimer !== null ||
      this.polling ||
      !this.allRows().some((row) => row.status === 'ingesting')
    ) {
      return;
    }
    this.pollTimer = setTimeout(() => {
      this.pollTimer = null;
      void this.pollRound();
    }, pollInterval);
  }

  /** Asks after the job of every ingesting row, and once all have answered, schedules the next round. */
  private async pollRound(): Promise<void> {
    const generation = this.pollGeneration;
    this.polling = true;
    const ingesting = this.allRows().filter((row) => row.status === 'ingesting' && row.jobId);
    await Promise.all(ingesting.map((row) => this.poll(row.id, row.jobId!)));
    if (generation !== this.pollGeneration) {
      return;
    }
    this.polling = false;
    this.schedulePoll();
  }

  /** Moves an ingesting row on by what its job says. A poll that fails leaves the row for the next round. */
  private async poll(id: number, jobId: string): Promise<void> {
    let change: Partial<UploadRow>;
    try {
      const job = await this.jobs.get(jobId);
      change = jobChange(job);
    } catch (error) {
      if (!(error instanceof HttpErrorResponse && error.status === 404)) {
        return;
      }
      change = { status: 'failed', error: jobGone };
    }
    // The row may have been removed, or moved on some other way, while the poll was out.
    if (this.allRows().find((row) => row.id === id)?.status === 'ingesting') {
      this.change(id, change);
    }
  }

  private stopPolling(): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
    this.polling = false;
    this.pollGeneration++;
  }

  private isFailed(id: number): boolean {
    return this.allRows().find((row) => row.id === id)?.status === 'failed';
  }

  /** Changes the row only if it is still there and still failed, and says whether it did. */
  private changeFailed(id: number, change: Partial<UploadRow>): boolean {
    if (!this.isFailed(id)) {
      return false;
    }
    this.change(id, change);
    return true;
  }

  private change(id: number, change: Partial<UploadRow>): void {
    this.allRows.update((rows) => rows.map((row) => (row.id === id ? { ...row, ...change } : row)));
  }
}

/** Whether the row's file is held, newly added or already there, with nothing left to do for it. */
export function isFinished(row: UploadRow): boolean {
  return row.status === 'added' || row.status === 'duplicate';
}

/** How often the jobs of ingesting rows are asked after, in milliseconds. */
const pollInterval = 1000;

/** Why a row fails when its job was deleted before it finished. */
const jobGone = 'The job no longer exists.';

/** What a job's state means for its row: nothing while it is pending or running. */
function jobChange(job: JobDetailResponse): Partial<UploadRow> {
  switch (job.status) {
    case 'succeeded': {
      const result = job.result as IngestResult;
      return {
        status: result.outcome === 'duplicate' ? 'duplicate' : 'added',
        workId: result.workId,
      };
    }
    case 'failed':
      return { status: 'failed', error: job.error ?? somethingWentWrong };
    default:
      return {};
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
