import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { components } from '../../api/schema';
import { WorkCover } from './work-cover';

type WorkSummaryResponse = components['schemas']['WorkSummaryResponse'];

/** A work in a grid: one link to its page that holds its small cover, its title and its authors. */
@Component({
  selector: 'app-work-card',
  host: { class: 'block' },
  imports: [RouterLink, WorkCover],
  template: `
    @let shown = work();
    <a class="group flex flex-col gap-1" [routerLink]="['/works', shown.id]">
      <app-work-cover class="mb-1" [work]="shown" size="small" />
      <span class="line-clamp-2 text-sm font-medium group-hover:underline">{{ shown.title }}</span>
      <span class="text-muted-foreground line-clamp-2 text-xs">{{ shown.authors.join(', ') }}</span>
    </a>
  `,
})
export class WorkCard {
  readonly work = input.required<WorkSummaryResponse>();
}
