import { Injectable, computed, inject } from '@angular/core';
import { UploadQueue } from '../upload/upload-queue';
import { ActivitySummary } from './activity-summary';
import { UploadActivity } from './upload-activity';

/**
 * What the Uploads section has the Notifications button's badge say. It is apart from the section because the badge
 * shows while the popover, and the section in it, is closed.
 */
@Injectable({ providedIn: 'root' })
export class UploadsSummary {
  private readonly queue = inject(UploadQueue);
  private readonly activity = inject(UploadActivity);

  /** How many uploads there are, in flight and failed, counting this tab's files and the server's uploads. */
  readonly summary = computed<ActivitySummary>(() => {
    let failed = 0;
    let inFlight = 0;
    for (const row of this.queue.rows()) {
      if (row.status === 'failed') {
        failed++;
      } else {
        inFlight++;
      }
    }
    for (const row of this.activity.rows()) {
      if (row.status === 'failed') {
        failed++;
      } else if (row.status === 'queued' || row.status === 'running') {
        inFlight++;
      }
    }
    return { items: this.queue.rows().length + this.activity.rows().length, inFlight, failed };
  });
}
