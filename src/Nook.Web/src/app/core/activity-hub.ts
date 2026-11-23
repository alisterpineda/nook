import { Injectable, InjectionToken, inject } from '@angular/core';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { Observable, Subject } from 'rxjs';
import { components } from '../api/schema';

type JobSummaryResponse = components['schemas']['JobSummaryResponse'];
type UploadActivityResponse = components['schemas']['UploadActivityResponse'];

/** How long to wait, in milliseconds, before each try at getting a connection. Every try after the last waits as long as it. */
const retryDelays = [0, 2000, 10000, 30000];

function retryDelay(earlierTries: number): number {
  return retryDelays[Math.min(earlierTries, retryDelays.length - 1)];
}

/** Makes the builder of the hub's connection. A token, so a spec can stand in for SignalR. */
export const HUB_CONNECTION_BUILDER = new InjectionToken<() => HubConnectionBuilder>(
  'HUB_CONNECTION_BUILDER',
  { providedIn: 'root', factory: () => () => new HubConnectionBuilder() },
);

/**
 * The connection to the API's activity hub, which pushes changes to the user's own uploads and, to an admin, to
 * every job. It is the only place that talks to SignalR. A connection that drops, or that could not be opened, is
 * tried again until `stop()`. Messages sent while the connection is down are not replayed, so whoever follows the
 * hub loads again on `reconnected`.
 */
@Injectable({ providedIn: 'root' })
export class ActivityHub {
  private readonly builder = inject(HUB_CONNECTION_BUILDER);
  private connection: HubConnection | null = null;
  /** How the wanted connection's opening went or is going, which every `start()` for it answers with. */
  private opening: Promise<void> = Promise.resolve();
  /** The wait before the next try at opening a connection that has never opened. */
  private retry: ReturnType<typeof setTimeout> | null = null;
  private readonly uploadChangedMessages = new Subject<UploadActivityResponse>();
  private readonly uploadRemovedMessages = new Subject<string>();
  private readonly jobChangedMessages = new Subject<JobSummaryResponse>();
  private readonly jobRemovedMessages = new Subject<string>();
  private readonly reconnectedMessages = new Subject<void>();

  /** One of the user's uploads changed where it stands. */
  readonly uploadChanged: Observable<UploadActivityResponse> =
    this.uploadChangedMessages.asObservable();

  /** The job of one of the user's uploads was deleted, with the id of the job. */
  readonly uploadRemoved: Observable<string> = this.uploadRemovedMessages.asObservable();

  /** A job changed status. Sent to admins. */
  readonly jobChanged: Observable<JobSummaryResponse> = this.jobChangedMessages.asObservable();

  /** A job was deleted, with its id. Sent to admins. */
  readonly jobRemoved: Observable<string> = this.jobRemovedMessages.asObservable();

  /** The connection came back after a drop, or opened after failing to, so changes sent meanwhile are lost. */
  readonly reconnected: Observable<void> = this.reconnectedMessages.asObservable();

  /**
   * Opens the connection. While one is open or being tried this opens nothing and answers as the call that began
   * it did, so whoever asks can wait for the connection to be open. When it cannot be opened this rejects, and the
   * connection is tried again in the background, with `reconnected` sent once it opens.
   */
  start(): Promise<void> {
    if (!this.connection) {
      this.opening = this.open();
    }
    return this.opening;
  }

  private async open(): Promise<void> {
    const connection = this.builder()
      .withUrl('/api/hubs/activity')
      // Never gives up: an API that is away for a while must not leave the tab deaf until it is reloaded.
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: ({ previousRetryCount }) => retryDelay(previousRetryCount),
      })
      .build();
    connection.on('UploadChanged', (upload: UploadActivityResponse) =>
      this.uploadChangedMessages.next(upload),
    );
    connection.on('UploadRemoved', (jobId: string) => this.uploadRemovedMessages.next(jobId));
    connection.on('JobChanged', (job: JobSummaryResponse) => this.jobChangedMessages.next(job));
    connection.on('JobRemoved', (jobId: string) => this.jobRemovedMessages.next(jobId));
    connection.onreconnected(() => this.reconnectedMessages.next());
    this.connection = connection;
    try {
      await connection.start();
    } catch (error) {
      this.startAgain(connection, 1);
      throw error;
    }
  }

  /** Closes the connection. Does nothing when none is open. */
  async stop(): Promise<void> {
    const connection = this.connection;
    this.connection = null;
    if (this.retry !== null) {
      clearTimeout(this.retry);
      this.retry = null;
    }
    await connection?.stop();
  }

  /** Tries to open `connection` after a wait, and keeps trying while it is still the one wanted. */
  private startAgain(connection: HubConnection, earlierTries: number): void {
    if (this.connection !== connection) {
      return;
    }
    this.retry = setTimeout(() => {
      this.retry = null;
      connection.start().then(
        () => {
          if (this.connection === connection) {
            this.opening = Promise.resolve();
            this.reconnectedMessages.next();
          }
        },
        () => this.startAgain(connection, earlierTries + 1),
      );
    }, retryDelay(earlierTries));
  }
}
