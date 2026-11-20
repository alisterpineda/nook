import { Component, computed, input } from '@angular/core';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { LoadingRegion } from '../../ui/loading';
import { workGrid } from './work-grid';

/** The shape of a grid of works, shown while the works are on their way. */
@Component({
  selector: 'app-work-grid-skeleton',
  host: { class: 'block' },
  imports: [HlmSkeletonImports, LoadingRegion],
  template: `
    <app-loading [class]="workGrid">
      @for (card of cards(); track card) {
        <div class="flex flex-col gap-2" aria-hidden="true">
          <div hlmSkeleton class="aspect-[2/3] w-full"></div>
          <div hlmSkeleton class="h-4 w-4/5"></div>
          <div hlmSkeleton class="h-3 w-1/2"></div>
        </div>
      }
    </app-loading>
  `,
})
export class WorkGridSkeleton {
  /** How many works to stand in for. */
  readonly count = input(12);

  protected readonly workGrid = workGrid;
  protected readonly cards = computed(() =>
    Array.from({ length: this.count() }, (_, index) => index),
  );
}
