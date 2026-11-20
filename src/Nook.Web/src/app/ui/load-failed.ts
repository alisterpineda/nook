import { Component, output } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideRotateCw } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { somethingWentWrong } from '../core/messages';

/** What a page shows in place of what it could not load, with a button that asks for it again. */
@Component({
  selector: 'app-load-failed',
  host: { class: 'block' },
  imports: [NgIcon, HlmButtonImports, HlmEmptyImports],
  providers: [provideIcons({ lucideCircleAlert, lucideRotateCw })],
  template: `
    <div hlmEmpty>
      <div hlmEmptyHeader>
        <div hlmEmptyMedia variant="icon" class="text-destructive">
          <ng-icon name="lucideCircleAlert" />
        </div>
        <div hlmEmptyTitle>Could not load this</div>
        <p hlmEmptyDescription role="alert">{{ somethingWentWrong }}</p>
      </div>
      <div hlmEmptyContent>
        <button hlmBtn variant="outline" size="sm" type="button" (click)="retry.emit()">
          <ng-icon name="lucideRotateCw" />
          Retry
        </button>
      </div>
    </div>
  `,
})
export class LoadFailed {
  /** The user asked to load it again. */
  readonly retry = output<void>();

  protected readonly somethingWentWrong = somethingWentWrong;
}
