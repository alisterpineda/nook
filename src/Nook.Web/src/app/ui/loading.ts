import { Component } from '@angular/core';

/**
 * Wraps the shapes that stand in for what is on its way. Its classes lay them out, and it is busy to a screen reader,
 * which hears "Loading" in place of the shapes inside. The shapes themselves are hidden from it by whoever draws them.
 */
@Component({
  selector: 'app-loading',
  host: { 'aria-busy': 'true' },
  template: `
    <span class="sr-only">Loading</span>
    <ng-content />
  `,
})
export class LoadingRegion {}
