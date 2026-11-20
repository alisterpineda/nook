import { Injectable } from '@angular/core';
import { Subject } from 'rxjs';
import { components } from '../api/schema';
import { ActivityHub } from '../core/activity-hub';

type JobSummaryResponse = components['schemas']['JobSummaryResponse'];
type UploadActivityResponse = components['schemas']['UploadActivityResponse'];

/**
 * Stands in for `ActivityHub` in specs: it opens no connection, and a spec sends the messages by hand. Provide it
 * with `provideFakeActivityHub()`.
 */
@Injectable()
export class FakeActivityHub {
  readonly uploadChanged = new Subject<UploadActivityResponse>();
  readonly uploadRemoved = new Subject<string>();
  readonly jobChanged = new Subject<JobSummaryResponse>();
  readonly jobRemoved = new Subject<string>();
  readonly reconnected = new Subject<void>();

  /** Whether the connection is open. */
  open = false;

  async start(): Promise<void> {
    this.open = true;
  }

  async stop(): Promise<void> {
    this.open = false;
  }
}

/** Providers that swap the real hub for a `FakeActivityHub`, which `TestBed.inject(FakeActivityHub)` returns. */
export function provideFakeActivityHub() {
  return [FakeActivityHub, { provide: ActivityHub, useExisting: FakeActivityHub }];
}
