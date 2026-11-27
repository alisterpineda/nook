import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmProgressImports } from '@spartan-ng/helm/progress';
import { components } from '../../api/schema';
import { UploadQueue } from '../upload/upload-queue';
import { UploadActivity } from './upload-activity';

type UploadActivityResponse = components['schemas']['UploadActivityResponse'];

/** What a failed upload says for each kind of failure; a failure with no kind reads as an internal one. */
const failureSentences = {
  unsupportedFormat: 'This file is not a supported book format.',
  unreadable: 'This file could not be read. It may be damaged.',
  internal: 'Something went wrong while adding this file.',
} as const;

const statusLabels: Record<UploadActivityResponse['status'], string> = {
  queued: 'Queued',
  running: 'Ingesting',
  added: 'Added',
  duplicate: 'Duplicate',
  failed: 'Failed',
};

/** One line of the list, whether the file is still in this tab or the server holds its upload. */
interface Entry {
  key: string;
  name: string;
  label: string;
  /** How much of the file has been sent, while it is being sent. */
  percent?: number;
  /** The work an added or duplicate upload leads to. */
  workId: string | null;
  failed: boolean;
  /** Why the upload failed, in words fit for the person who made it. */
  reason?: string;
  /** What went wrong with the last retry or removal of it, when something did. */
  problem?: string;
  retry(): void;
  remove(): void;
}

/**
 * The Uploads section of the Notifications popover. It lists the files in this tab that are waiting, being sent or
 * refused, above the uploads the server holds, and of those the failed ones before the others. The popover leaves it
 * out while there is nothing to list.
 */
@Component({
  selector: 'app-uploads-section',
  imports: [RouterLink, HlmButtonImports, HlmProgressImports],
  host: { class: 'flex flex-col gap-2' },
  template: `
    <h3 class="text-muted-foreground text-xs font-medium">Uploads</h3>
    <ul class="flex max-h-96 flex-col gap-3 overflow-y-auto">
      @for (entry of entries(); track entry.key) {
        <li class="flex flex-col gap-1">
          <span class="truncate font-medium" [title]="entry.name">{{ entry.name }}</span>
          <span class="text-muted-foreground text-xs">
            @if (entry.workId) {
              <a class="text-foreground underline" [routerLink]="['/works', entry.workId]">{{
                entry.label
              }}</a>
            } @else {
              <span [class.text-destructive]="entry.failed">{{ entry.label }}</span>
            }
            @if (entry.percent !== undefined) {
              {{ entry.percent }}%
            }
          </span>
          @if (entry.percent !== undefined) {
            <hlm-progress [value]="entry.percent" aria-label="Upload progress">
              <hlm-progress-indicator />
            </hlm-progress>
          }
          @if (entry.failed) {
            <p class="text-destructive text-xs">{{ entry.reason }}</p>
            @if (entry.problem) {
              <p class="text-destructive text-xs" role="alert">{{ entry.problem }}</p>
            }
            <div class="flex gap-2">
              <button hlmBtn variant="outline" size="sm" type="button" (click)="entry.retry()">
                Retry
              </button>
              <button hlmBtn variant="ghost" size="sm" type="button" (click)="entry.remove()">
                Remove
              </button>
            </div>
          }
        </li>
      }
    </ul>
  `,
})
export class UploadsSection {
  private readonly queue = inject(UploadQueue);
  private readonly activity = inject(UploadActivity);

  /** The files of this tab, then the server's failed uploads, then its others. */
  protected readonly entries = computed<Entry[]>(() => [
    ...this.queue.rows().map((row): Entry => {
      const failed = row.status === 'failed';
      return {
        key: `tab-${row.id}`,
        name: row.name,
        label: failed ? 'Failed' : row.status === 'waiting' ? 'Waiting' : 'Uploading',
        percent: row.status === 'uploading' ? (row.percent ?? 0) : undefined,
        workId: null,
        failed,
        reason: row.error,
        retry: () => this.queue.retry(row.id),
        remove: () => this.queue.remove(row.id),
      };
    }),
    ...this.serverEntries().filter((entry) => entry.failed),
    ...this.serverEntries().filter((entry) => !entry.failed),
  ]);

  /** The server's uploads, in the list's order. */
  private readonly serverEntries = computed<Entry[]>(() =>
    this.activity.rows().map((row) => ({
      key: `server-${row.jobId}`,
      name: row.fileName,
      label: statusLabels[row.status],
      workId: row.status === 'added' || row.status === 'duplicate' ? row.workId : null,
      failed: row.status === 'failed',
      reason: failureSentences[row.failure ?? 'internal'],
      problem: this.activity.problems()[row.jobId],
      retry: () => void this.activity.retry(row.jobId),
      remove: () => void this.activity.remove(row.jobId),
    })),
  );
}
