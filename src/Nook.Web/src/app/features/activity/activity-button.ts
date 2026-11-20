import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideActivity } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { HlmProgressImports } from '@spartan-ng/helm/progress';
import { ActivityApi } from '../../api/activity-api';
import { components } from '../../api/schema';
import { somethingWentWrong } from '../../core/messages';
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
  retry(): void;
  remove(): void;
}

/**
 * The header's Activity button. Its popover lists the files in this tab that are waiting, being sent or refused,
 * above the uploads the server holds. Its badge counts the uploads in flight, or, when any has failed, the ones that
 * failed.
 */
@Component({
  selector: 'app-activity-button',
  imports: [
    NgIcon,
    RouterLink,
    HlmBadgeImports,
    HlmButtonImports,
    HlmPopoverImports,
    HlmProgressImports,
  ],
  providers: [provideIcons({ lucideActivity })],
  template: `
    <hlm-popover align="end" sideOffset="4">
      <button hlmPopoverTrigger hlmBtn variant="outline" size="sm" type="button">
        <ng-icon name="lucideActivity" aria-hidden="true" />
        <span class="max-md:sr-only">Activity</span>
        @if (badge(); as badge) {
          <span hlmBadge [variant]="badge.failed ? 'destructive' : 'default'">{{
            badge.count
          }}</span>
        }
      </button>
      <ng-template hlmPopoverPortal>
        <hlm-popover-content class="w-96 max-w-[calc(100vw-2rem)]">
          <div hlmPopoverHeader>
            <h2 hlmPopoverTitle>Activity</h2>
          </div>
          @if (entries().length === 0) {
            <p class="text-muted-foreground">No uploads yet.</p>
          } @else {
            <ul class="flex max-h-96 flex-col gap-3 overflow-y-auto">
              @for (entry of entries(); track entry.key) {
                <li class="flex flex-col gap-1">
                  <span class="truncate font-medium" [title]="entry.name">{{ entry.name }}</span>
                  <span class="text-muted-foreground text-xs">
                    @if (entry.workId) {
                      <a
                        class="text-foreground underline"
                        [routerLink]="['/works', entry.workId]"
                        >{{ entry.label }}</a
                      >
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
                    @if (problems()[entry.key]; as problem) {
                      <p class="text-destructive text-xs" role="alert">{{ problem }}</p>
                    }
                    <div class="flex gap-2">
                      <button
                        hlmBtn
                        variant="outline"
                        size="sm"
                        type="button"
                        (click)="entry.retry()"
                      >
                        Retry
                      </button>
                      <button
                        hlmBtn
                        variant="ghost"
                        size="sm"
                        type="button"
                        (click)="entry.remove()"
                      >
                        Remove
                      </button>
                    </div>
                  }
                </li>
              }
            </ul>
          }
        </hlm-popover-content>
      </ng-template>
    </hlm-popover>
  `,
})
export class ActivityButton {
  private readonly queue = inject(UploadQueue);
  private readonly activity = inject(UploadActivity);
  private readonly api = inject(ActivityApi);

  /** What went wrong with a retry or removal, by the key of the row it was for. */
  protected readonly problems = signal<Record<string, string>>({});

  /** The files of this tab, then the server's uploads. */
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
    ...this.activity.rows().map((row): Entry => {
      const key = `server-${row.jobId}`;
      return {
        key,
        name: row.fileName,
        label: statusLabels[row.status],
        workId: row.status === 'added' || row.status === 'duplicate' ? row.workId : null,
        failed: row.status === 'failed',
        reason: failureSentences[row.failure ?? 'internal'],
        retry: () => void this.change(key, () => this.api.retry(row.jobId)),
        remove: () => void this.change(key, () => this.api.remove(row.jobId)),
      };
    }),
  ]);

  /** The number on the badge, or `null` for no badge: the failed uploads when there are any, else those in flight. */
  protected readonly badge = computed(() => {
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
    if (failed > 0) {
      return { count: failed, failed: true };
    }
    return inFlight > 0 ? { count: inFlight, failed: false } : null;
  });

  /** Runs `call` for a server row. The row itself changes through the hub, so only a failure is shown here. */
  private async change(key: string, call: () => Promise<void>): Promise<void> {
    this.problems.update((problems) => ({ ...problems, [key]: '' }));
    try {
      await call();
    } catch {
      this.problems.update((problems) => ({ ...problems, [key]: somethingWentWrong }));
    }
  }
}
