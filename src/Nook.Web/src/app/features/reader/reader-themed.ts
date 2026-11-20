import { Directive, computed, input } from '@angular/core';
import { ReaderTheme, readerThemeColours } from './reader-engine';

/**
 * Shows an element in a reader theme, whatever the app's: the element is marked with the theme, which gives it that
 * theme's colours, and its background and text, as a surface and as a popover, are the very colours the book is drawn
 * in. For what the reader draws outside its page (panels, lists), which the page's own colours do not reach.
 */
@Directive({
  selector: '[appReaderThemed]',
  host: {
    '[attr.data-reader-theme]': 'theme()',
    '[style.--background]': 'colours().background',
    '[style.--foreground]': 'colours().text',
    '[style.--popover]': 'colours().background',
    '[style.--popover-foreground]': 'colours().text',
  },
})
export class ReaderThemed {
  readonly theme = input.required<ReaderTheme>({ alias: 'appReaderThemed' });
  protected readonly colours = computed(() => readerThemeColours[this.theme()]);
}
