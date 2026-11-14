import { Component, computed, input, linkedSignal } from '@angular/core';
import { components } from '../../api/schema';
import { CoverSize, coverUrl } from '../../api/works-api';

type WorkSummaryResponse = components['schemas']['WorkSummaryResponse'];

/** What the cover needs to know about a work, which both a work's summary and its details have. */
export type WorkCoverSubject = Pick<WorkSummaryResponse, 'id' | 'title' | 'coverHash'>;

/**
 * A work's cover at a size, filling the width it is given at the shape of a book. A work with no cover, or whose cover
 * fails to load, shows a placeholder with its title instead.
 */
@Component({
  selector: 'app-work-cover',
  host: { class: 'block' },
  template: `
    <div class="bg-muted aspect-[2/3] w-full overflow-hidden rounded-md">
      @let url = imageUrl();
      @if (url !== null && !failed()) {
        <img
          class="h-full w-full object-cover"
          alt=""
          loading="lazy"
          [src]="url"
          (error)="failed.set(true)"
        />
      } @else {
        <div
          class="text-muted-foreground flex h-full w-full items-center justify-center p-3 text-center text-sm font-medium break-words"
        >
          {{ work().title }}
        </div>
      }
    </div>
  `,
})
export class WorkCover {
  readonly work = input.required<WorkCoverSubject>();
  readonly size = input.required<CoverSize>();

  protected readonly imageUrl = computed(() => {
    const { id, coverHash } = this.work();
    return coverHash === null ? null : coverUrl(id, coverHash, this.size());
  });

  /** Whether the image at the current URL failed to load. A new URL gets a new try. */
  protected readonly failed = linkedSignal({ source: this.imageUrl, computation: () => false });
}
