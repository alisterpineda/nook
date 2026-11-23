import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Params, Router, isActive } from '@angular/router';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { readTerm, termParam } from '../../core/search-term';

/** How long typing must stop before the box searches. */
const pauseMs = 300;

/**
 * The search box on the library page. It searches everything the caller can see and shows its matches in the list
 * of works at `/library?q=<term>`: after a pause in typing, or at once on Enter. It shows the term the address names
 * while the list is open, and is empty on every other page. Emptying it, by hand or with its clear button, ends a
 * search and shows the whole library.
 */
@Component({
  selector: 'app-search-box',
  imports: [HlmInputImports],
  template: `
    <div class="relative">
      <label class="sr-only" for="library-search">Search the library</label>
      <!-- The browser's own clear control is hidden, so the box has one clear button in every browser. -->
      <input
        hlmInput
        id="library-search"
        type="search"
        autocomplete="off"
        class="w-full pr-8 [&::-webkit-search-cancel-button]:appearance-none"
        placeholder="Search"
        [value]="text()"
        (input)="typed($event)"
        (keydown.enter)="searchNow()"
      />
      @if (holdsText()) {
        <button
          type="button"
          class="text-muted-foreground hover:text-foreground absolute inset-y-0 right-0 w-8"
          aria-label="Clear search"
          (click)="clear()"
        >
          <span aria-hidden="true">&times;</span>
        </button>
      }
    </div>
  `,
})
export class SearchBox {
  private readonly router = inject(Router);
  private readonly queryParams = toSignal(inject(ActivatedRoute).queryParamMap, {
    requireSync: true,
  });
  private readonly onLibrary = isActive('/library', this.router, { queryParams: 'ignored' });
  /** The term the address names while the list of works is open, and nothing on any other page. */
  private readonly addressTerm = computed(() =>
    this.onLibrary() ? (readTerm(this.queryParams()) ?? '') : '',
  );

  private timer: ReturnType<typeof setTimeout> | undefined;
  /** The term the box last asked the address to name, until the address names another. */
  private sent: string | null = null;

  protected readonly text = signal('');
  /** Whether there is anything to clear: text that is only spaces counts as none. */
  protected readonly holdsText = computed(() => this.text().trim() !== '');

  constructor() {
    // An address that only confirms what the box sent leaves the text alone: by then the user may have typed on, if
    // only a space at the end. Any other address is one the user went to, and the box shows its term.
    effect(() => {
      const term = this.addressTerm();
      if (term !== this.sent) {
        this.sent = null;
        this.text.set(term);
      }
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
  }

  protected typed(event: Event): void {
    this.text.set((event.target as HTMLInputElement).value);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.search(), pauseMs);
  }

  protected searchNow(): void {
    clearTimeout(this.timer);
    this.search();
  }

  /** Empties the box, ending the search when one is showing. */
  protected clear(): void {
    this.text.set('');
    this.searchNow();
  }

  private search(): void {
    const term = this.text().trim();
    // Refining or ending a search replaces it, so back leaves the search instead of stepping through each term.
    const searching = this.addressTerm() !== '';
    if (term !== '') {
      this.go(term, { queryParams: { [termParam]: term }, replaceUrl: searching });
    } else if (searching) {
      // An empty box ends the search that is showing. Where none is showing there is nothing to end.
      this.go(term, { replaceUrl: true });
    }
  }

  private go(term: string, extras: { queryParams?: Params; replaceUrl: boolean }): void {
    this.sent = term;
    const forget = () => {
      // A navigation that never arrived confirms nothing, unless a later one from the box took its place.
      if (this.sent === term && this.addressTerm() !== term) {
        this.sent = null;
      }
    };
    this.router.navigate(['/library'], extras).then((arrived) => arrived || forget(), forget);
  }
}
