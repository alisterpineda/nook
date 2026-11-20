import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSearchX } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';

/** What a page shows in place of something the API does not have, with a link back to the list it would be in. */
@Component({
  selector: 'app-not-found',
  host: { class: 'block' },
  imports: [NgIcon, RouterLink, HlmButtonImports, HlmEmptyImports],
  providers: [provideIcons({ lucideSearchX })],
  template: `
    <div hlmEmpty>
      <div hlmEmptyHeader>
        <div hlmEmptyMedia variant="icon"><ng-icon name="lucideSearchX" /></div>
        <h1 hlmEmptyTitle>{{ heading() }}</h1>
        <p hlmEmptyDescription>It may have been removed, or the link may be wrong.</p>
      </div>
      <div hlmEmptyContent>
        <a hlmBtn variant="outline" size="sm" [routerLink]="backLink()">{{ backLabel() }}</a>
      </div>
    </div>
  `,
})
export class NotFound {
  /** What is missing, such as "Work not found". */
  readonly heading = input.required<string>();
  /** The list the missing thing would be in. */
  readonly backLink = input.required<string>();
  /** What the link to that list says. */
  readonly backLabel = input.required<string>();
}
