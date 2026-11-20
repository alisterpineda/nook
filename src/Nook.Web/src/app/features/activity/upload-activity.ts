import { Injectable, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { ActivityApi } from '../../api/activity-api';
import { components } from '../../api/schema';
import { ActivityHub } from '../../core/activity-hub';

type UploadActivityResponse = components['schemas']['UploadActivityResponse'];

/** How many uploads the list holds, as many as the server lists. */
const maxRows = 20;

/**
 * The signed-in user's recent uploads as the server holds them, newest first, kept current from the activity hub.
 * It is provided in the root so the list is there whichever page is open. `start()` loads the list and opens the
 * hub, and `stop()` closes the hub and empties the list, so nothing carries over to the next user.
 */
@Injectable({ providedIn: 'root' })
export class UploadActivity {
  private readonly api = inject(ActivityApi);
  private readonly hub = inject(ActivityHub);
  private readonly allRows = signal<readonly UploadActivityResponse[]>([]);
  private following: Subscription | null = null;
  /** Counts loads, and changes on `stop()`, so an answer to an older load or to one from before a stop is let go. */
  private generation = 0;
  /** How many lists are on their way. */
  private loading = 0;
  /**
   * What the hub said while a list was on its way, oldest first. A list is what the server held when it was asked,
   * so what the hub said after that is newer and is applied again on top of the answer.
   */
  private said: ((rows: readonly UploadActivityResponse[]) => readonly UploadActivityResponse[])[] =
    [];

  /** The uploads, newest first. */
  readonly rows = this.allRows.asReadonly();

  /** Loads the list and starts following the hub. Does nothing while already started. */
  start(): void {
    if (this.following) {
      return;
    }
    const following = new Subscription();
    following.add(this.hub.uploadChanged.subscribe((upload) => this.change(upload)));
    following.add(this.hub.uploadRemoved.subscribe((jobId) => this.remove(jobId)));
    following.add(this.hub.reconnected.subscribe(() => void this.load()));
    this.following = following;

    const generation = this.generation;
    // The hub opens first, so a change made while the list is on its way is heard, and kept over the list's answer.
    // The hub being down is no reason to show nothing: what the server holds can still be listed.
    const loadUnlessStopped = () => (this.generation === generation ? this.load() : undefined);
    void this.hub.start().then(loadUnlessStopped, loadUnlessStopped);
  }

  /** Stops following the hub, closes it and empties the list. */
  async stop(): Promise<void> {
    this.generation++;
    this.following?.unsubscribe();
    this.following = null;
    this.said = [];
    this.allRows.set([]);
    try {
      await this.hub.stop();
    } catch {
      // A connection that will not close cleanly is gone all the same.
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
          .reduce<readonly UploadActivityResponse[]>(
            (rows, apply) => apply(rows),
            uploads.slice(0, maxRows),
          ),
      );
    }
    if (--this.loading === 0) {
      this.said = [];
    }
  }

  private change(upload: UploadActivityResponse): void {
    this.apply((rows) =>
      rows.some((row) => row.jobId === upload.jobId)
        ? rows.map((row) => (row.jobId === upload.jobId ? upload : row))
        : [upload, ...rows].slice(0, maxRows),
    );
  }

  private remove(jobId: string): void {
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
