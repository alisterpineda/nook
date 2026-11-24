import {
  Component,
  afterRenderEffect,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBook, lucideSearch } from '@ng-icons/lucide';
import { Router } from '@angular/router';
import { BrnCommand } from '@spartan-ng/brain/command';
import { BrnDialogState } from '@spartan-ng/brain/dialog';
import { HlmCommandImports } from '@spartan-ng/helm/command';
import { SearchApi, SearchHit, SearchResponse, SearchSource } from '../api/search-api';
import { searchSources } from './search-sources';

/** How long typing must pause before the server is asked. */
const pauseMs = 300;
/** The fewest characters, once trimmed, that a search is made for. */
const minTermLength = 2;
const resultsPerGroup = 6;

/** What the server answered for a term: its groups, or that the request failed. */
interface Answer {
  term: string;
  response: SearchResponse | null;
}

/** The server has already matched the hits, so none is hidden by the command's own text filter. */
const showEveryHit = (): boolean => true;

/**
 * The quick jump palette: a dialog with a search input, opened from the header's Search buttons or with Cmd+K or
 * Ctrl+K. It opens with an empty input and a hint, even when it was closed with a term typed.
 */
@Component({
  selector: 'app-command-palette',
  imports: [HlmCommandImports, NgIcon],
  providers: [provideIcons({ lucideBook, lucideSearch })],
  host: { '(document:keydown)': 'onKeydown($event)' },
  template: `
    <hlm-command-dialog
      title="Search"
      description="Search the library and jump to a result"
      dialogContentClass="w-[min(36rem,calc(100vw-2rem))] p-0"
      [state]="opened() ? 'open' : 'closed'"
      (stateChange)="onStateChange($event)"
    >
      <hlm-command [search]="term()" [filter]="showEveryHit" (searchChange)="term.set($event)">
        <hlm-command-input placeholder="Search…" />
        <hlm-command-list>
          @switch (state()) {
            @case ('hint') {
              <p class="text-muted-foreground py-6 text-center text-sm">
                Type to search the library
              </p>
            }
            @case ('loading') {
              <p class="text-muted-foreground py-6 text-center text-sm">Searching…</p>
            }
            @case ('error') {
              <p class="text-muted-foreground py-6 text-center text-sm">
                Something went wrong. Try again.
              </p>
            }
            @case ('empty') {
              <p class="text-muted-foreground py-6 text-center text-sm">No results found.</p>
            }
          }
          @if (answered(); as groups) {
            @for (group of groups; track group.source) {
              <hlm-command-group>
                <hlm-command-group-label>{{ sources[group.source].label }}</hlm-command-group-label>
                @for (hit of group.results; track hit.id) {
                  <button
                    hlmCommandItem
                    [value]="group.source + ':' + hit.id"
                    (selected)="openHit(group.source, hit)"
                  >
                    <ng-icon [name]="sources[group.source].icon" aria-hidden="true" />
                    <span class="flex min-w-0 flex-col text-start">
                      <span class="truncate">{{ hit.title }}</span>
                      @if (hit.subtitle) {
                        <span class="text-muted-foreground truncate text-xs">{{
                          hit.subtitle
                        }}</span>
                      }
                    </span>
                  </button>
                }
                @if (group.total > group.results.length && sources[group.source].viewAll) {
                  <button
                    hlmCommandItem
                    [value]="'viewall:' + group.source"
                    (selected)="viewAll(group.source)"
                  >
                    <ng-icon name="lucideSearch" aria-hidden="true" />
                    <span>View all ({{ group.total }}) in {{ sources[group.source].label }}</span>
                  </button>
                }
              </hlm-command-group>
            }
          }
        </hlm-command-list>
      </hlm-command>
    </hlm-command-dialog>
  `,
})
export class CommandPalette {
  protected readonly opened = signal(false);
  /** What the input holds. */
  protected readonly term = signal('');
  protected readonly showEveryHit = showEveryHit;

  protected readonly sources = searchSources;

  private readonly searchApi = inject(SearchApi);
  private readonly router = inject(Router);
  private readonly trimmed = computed(() => this.term().trim());
  /** The answer to the term that is still being shown, if one has come. */
  private readonly answer = signal<Answer | null>(null);

  protected readonly state = computed<'hint' | 'loading' | 'error' | 'empty' | 'results'>(() => {
    const term = this.trimmed();
    if (term.length < minTermLength) {
      return 'hint';
    }
    const answer = this.answer();
    if (answer?.term !== term) {
      return 'loading';
    }
    if (!answer.response) {
      return 'error';
    }
    return answer.response.groups.length === 0 ? 'empty' : 'results';
  });

  /** The groups to list, once an answer for the current term has come. */
  protected readonly answered = computed(() =>
    this.state() === 'results' ? (this.answer()?.response?.groups ?? null) : null,
  );

  private readonly command = viewChild(BrnCommand);

  constructor() {
    // The command activates a row when the term changes, not when rows arrive later.
    afterRenderEffect(() => {
      if (this.answered()) {
        this.command()?.keyManager.setFirstItemActive();
      }
    });

    effect((onCleanup) => {
      const term = this.trimmed();
      untracked(() => this.answer.set(null));
      if (term.length < minTermLength) {
        return;
      }
      const timer = setTimeout(() => void this.search(term), pauseMs);
      onCleanup(() => clearTimeout(timer));
    });
  }

  /** Asks the server for a term. The request cannot be cancelled, so an answer for a term the user has left is dropped. */
  private async search(term: string): Promise<void> {
    let response: SearchResponse | null;
    try {
      response = await this.searchApi.search({ Q: term, Limit: resultsPerGroup });
    } catch {
      response = null;
    }
    if (this.trimmed() === term) {
      this.answer.set({ term, response });
    }
  }

  open(): void {
    this.opened.set(true);
  }

  protected onStateChange(state: BrnDialogState): void {
    this.opened.set(state === 'open');
    if (state !== 'open') {
      this.term.set('');
    }
  }

  protected openHit(source: SearchSource, hit: SearchHit): void {
    void this.router.navigate(searchSources[source].open(hit));
    this.onStateChange('closed');
  }

  protected viewAll(source: SearchSource): void {
    const target = searchSources[source].viewAll?.(this.trimmed());
    if (target) {
      void this.router.navigate(target.commands, { queryParams: target.queryParams });
    }
    this.onStateChange('closed');
  }

  protected onKeydown(event: KeyboardEvent): void {
    if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      if (this.opened()) {
        this.onStateChange('closed');
      } else {
        this.open();
      }
    }
  }
}
