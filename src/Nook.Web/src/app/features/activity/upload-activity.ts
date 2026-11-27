import { Injectable, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { ActivityApi } from '../../api/activity-api';
import { components } from '../../api/schema';
import { ActivityHub } from '../../core/activity-hub';
import { somethingWentWrong } from '../../core/messages';

type UploadActivityResponse = components['schemas']['UploadActivityResponse'];

/** How many uploads that are not failed the list holds, as many as the server lists. */
const maxRows = 20;

/** The rows the list keeps: every failed one, and the first `maxRows` of the others. */
function kept(rows: readonly UploadActivityResponse[]): readonly UploadActivityResponse[] {
  let others = 0;
  return rows.filter((row) => row.status === 'failed' || ++others <= maxRows);
}

/**
 * The signed-in user's uploads as the server holds them, kept current from the activity hub: every failed upload and
 * the 20 newest others, each in the order the server gave, with an upload the hub names for the first time put first.
 * It is provided in the root so the list is there whichever page is open. `start()` loads the list and follows the
 * hub, and `stop()` lets go of the hub and empties the list, so nothing carries over to the next user. The hub's
 * connection is the shell's to open and close.
 */
@Injectable({ providedIn: 'root' })
export class UploadActivity {
  private readonly api = inject(ActivityApi);
  private readonly hub = inject(ActivityHub);
  private readonly allRows = signal<readonly UploadActivityResponse[]>([]);
  private following: Subscription | null = null;
  /** Counts loads, and changes on `stop()`, so an answer to an older load or to one from before a stop is let go. */
  private generation = 0;
  /** Counts stops, so the answer to a retry or removal from before a stop is let go. */
  private generationOfStops = 0;
  /** How many lists are on their way. */
  private loading = 0;
  /**
   * What the hub said while a list was on its way, oldest first. A list is what the server held when it was asked,
   * so what the hub said after that is newer and is applied again on top of the answer.
   */
  private said: ((rows: readonly UploadActivityResponse[]) => readonly UploadActivityResponse[])[] =
    [];

  /** The uploads, in the order they arrived. */
  readonly rows = this.allRows.asReadonly();

  private readonly allProblems = signal<Readonly<Record<string, string>>>({});

  /**
   * What went wrong with the last retry or removal of an upload, by the id of its job. It is kept here, not where
   * the uploads are shown, so it is still there for whoever looks after the answer has come.
   */
  readonly problems = this.allProblems.asReadonly();

  /** Loads the list and starts following the hub. Does nothing while already started. */
  start(): void {
    if (this.following) {
      return;
    }
    const following = new Subscription();
    following.add(this.hub.uploadChanged.subscribe((upload) => this.changed(upload)));
    following.add(this.hub.uploadRemoved.subscribe((jobId) => this.removed(jobId)));
    following.add(this.hub.reconnected.subscribe(() => void this.load()));
    this.following = following;

    const generation = this.generation;
    // The list waits for the hub to be open, so a change made while the list is on its way is heard, and kept over
    // the list's answer. Starting the hub opens nothing new when the shell has opened it already. The hub being
    // down is no reason to show nothing: what the server holds can still be listed.
    const loadUnlessStopped = () => (this.generation === generation ? this.load() : undefined);
    void this.hub.start().then(loadUnlessStopped, loadUnlessStopped);
  }

  /** Stops following the hub and empties the list. */
  stop(): void {
    this.generation++;
    this.generationOfStops++;
    this.following?.unsubscribe();
    this.following = null;
    this.said = [];
    this.allRows.set([]);
    this.allProblems.set({});
  }

  /** Runs a failed upload again. */
  retry(jobId: string): Promise<void> {
    return this.act(jobId, () => this.api.retry(jobId));
  }

  /** Removes a failed upload, along with the file it staged. */
  remove(jobId: string): Promise<void> {
    return this.act(jobId, () => this.api.remove(jobId));
  }

  /** Runs `call` for an upload. The row itself changes through the hub, so only a failure is kept here. */
  private async act(jobId: string, call: () => Promise<void>): Promise<void> {
    const generation = this.generationOfStops;
    this.allProblems.update((problems) => ({ ...problems, [jobId]: '' }));
    try {
      await call();
    } catch {
      // A failure that answers after a stop is the last user's, and is not kept for the next.
      if (generation === this.generationOfStops) {
        this.allProblems.update((problems) => ({ ...problems, [jobId]: somethingWentWrong }));
      }
    }
  }

  private async load(): Promise<void> {
    const generation = ++this.generation;
    const saidBefore = this.said.length;
    this.loading++;
    let uploads: UploadActivityResponse[] | null = null;
    try {
      uploads = await this.api.uploads();
    } catch {
      // The rows stay as they are, and the next reconnect or start asks again.
    }
    if (uploads && generation === this.generation) {
      this.allRows.set(
        this.said
          .slice(saidBefore)
          .reduce<readonly UploadActivityResponse[]>((rows, apply) => apply(rows), kept(uploads)),
      );
    }
    if (--this.loading === 0) {
      this.said = [];
    }
  }

  private changed(upload: UploadActivityResponse): void {
    this.apply((rows) =>
      kept(
        rows.some((row) => row.jobId === upload.jobId)
          ? rows.map((row) => (row.jobId === upload.jobId ? upload : row))
          : [upload, ...rows],
      ),
    );
  }

  private removed(jobId: string): void {
    this.apply((rows) => rows.filter((row) => row.jobId !== jobId));
  }

  /** Makes a change the hub named, and keeps it to make again over a list that is on its way. */
  private apply(
    change: (rows: readonly UploadActivityResponse[]) => readonly UploadActivityResponse[],
  ): void {
    if (this.loading > 0) {
      this.said.push(change);
    }
    this.allRows.update(change);
  }
}
