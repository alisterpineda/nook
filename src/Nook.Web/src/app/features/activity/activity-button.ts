import { Component, computed, inject } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBell } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { Session } from '../../core/session';
import { ActivitySummary } from './activity-summary';
import { UploadsSection } from './uploads-section';
import { UploadsSummary } from './uploads-summary';

/**
 * The header's Notifications button, a bell whose name is read out and shown on hover. Its popover holds one
 * section for each kind of activity the user has, and its badge adds up what the sections say: the things in
 * flight, or, when any has failed, the ones that failed. A section with nothing in it is left out. Someone who
 * could never have a section to see gets no button.
 */
@Component({
  selector: 'app-activity-button',
  imports: [NgIcon, HlmBadgeImports, HlmButtonImports, HlmPopoverImports, UploadsSection],
  providers: [provideIcons({ lucideBell })],
  template: `
    @if (hasSections()) {
      <hlm-popover align="end" sideOffset="4">
        <button
          hlmPopoverTrigger
          hlmBtn
          variant="outline"
          size="sm"
          type="button"
          title="Notifications"
        >
          <ng-icon name="lucideBell" aria-hidden="true" />
          <span class="sr-only">Notifications</span>
          @if (badge(); as badge) {
            <span hlmBadge [variant]="badge.failed ? 'destructive' : 'default'">{{
              badge.count
            }}</span>
          }
        </button>
        <ng-template hlmPopoverPortal>
          <hlm-popover-content class="w-96 max-w-[calc(100vw-2rem)]">
            <div hlmPopoverHeader>
              <h2 hlmPopoverTitle>Notifications</h2>
            </div>
            @if (showsUploads()) {
              <app-uploads-section />
            } @else {
              <p class="text-muted-foreground">No notifications yet.</p>
            }
          </hlm-popover-content>
        </ng-template>
      </hlm-popover>
    }
  `,
})
export class ActivityButton {
  private readonly session = inject(Session);
  private readonly uploads = inject(UploadsSummary);

  /** Whether the user could have uploads to see: only someone who may add books could. */
  private readonly mayUpload = this.session.canUpload;

  /** Whether the popover could ever hold a section for this user. */
  protected readonly hasSections = computed(() => this.mayUpload());

  /** What each section the user could see has the badge say. */
  private readonly summaries = computed<ActivitySummary[]>(() =>
    this.mayUpload() ? [this.uploads.summary()] : [],
  );

  /** Whether the Uploads section is in the popover: it is while there is an upload to list. */
  protected readonly showsUploads = computed(
    () => this.mayUpload() && this.uploads.summary().items > 0,
  );

  /** The number on the badge, or `null` for no badge: the failed things when there are any, else those in flight. */
  protected readonly badge = computed(() => {
    let failed = 0;
    let inFlight = 0;
    for (const summary of this.summaries()) {
      failed += summary.failed;
      inFlight += summary.inFlight;
    }
    if (failed > 0) {
      return { count: failed, failed: true };
    }
    return inFlight > 0 ? { count: inFlight, failed: false } : null;
  });
}
