import {
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgTemplateOutlet } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideAArrowDown,
  lucideAArrowUp,
  lucideArrowLeft,
  lucideChevronLeft,
  lucideChevronRight,
  lucideListTree,
  lucidePalette,
  lucideScrollText,
} from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSheetImports } from '@spartan-ng/helm/sheet';
import { publicationManifestUrl } from '../../api/publications-api';
import {
  READER_ENGINE,
  ReaderArrowKey,
  ReaderContentsEntry,
  ReaderEngine,
  ReaderPlace,
  ReaderSettings,
  ReaderTapZone,
  readerThemeColours,
  readerThemes,
} from './reader-engine';
import { ReaderPlaceStore } from './reader-place-store';
import { ReaderSettingsStore, readerFontSize } from './reader-settings-store';

const couldNotShow = 'This book could not be shown.';

/** Where the page stands with the book. */
type Status = 'opening' | 'open' | 'failed';

/**
 * One book, full-screen: a toolbar with the way back to its work, the book's table of contents, how the book is shown
 * (layout, font size and theme) and the page turns, the book itself, which the reader engine renders and moves through,
 * and a readout of how far through the book and which chapter the reader is in.
 * The book opens where it was last left in this browser, and the place is kept as the reader moves. How books are shown
 * is kept in this browser too, the same for every book, and the reader's own colours follow the book's theme.
 */
@Component({
  selector: 'app-reader-page',
  imports: [HlmButtonImports, HlmSheetImports, NgIcon, NgTemplateOutlet, RouterLink],
  providers: [
    provideIcons({
      lucideAArrowDown,
      lucideAArrowUp,
      lucideArrowLeft,
      lucideChevronLeft,
      lucideChevronRight,
      lucideListTree,
      lucidePalette,
      lucideScrollText,
    }),
  ],
  host: {
    class: 'bg-background text-foreground flex h-dvh flex-col',
    '[attr.data-reader-theme]': 'settings().theme',
    '[style.--background]': 'colours().background',
    '[style.--foreground]': 'colours().text',
  },
  // The toolbar's controls are icons, their names read out and shown on hover, so that all of them fit on a phone.
  template: `
    <header class="flex items-center gap-2 border-b p-2">
      <a
        hlmBtn
        variant="ghost"
        size="icon-sm"
        title="Back to the work"
        [routerLink]="['/works', workId()]"
      >
        <ng-icon name="lucideArrowLeft" />
        <span class="sr-only">Back to the work</span>
      </a>
      @if (status() !== 'failed') {
        @if (contents().length > 0) {
          <button
            hlmBtn
            variant="outline"
            size="icon-sm"
            type="button"
            title="Contents"
            (click)="contentsOpen.set(true)"
          >
            <ng-icon name="lucideListTree" />
            <span class="sr-only">Contents</span>
          </button>
          <hlm-sheet
            side="left"
            [state]="contentsOpen() ? 'open' : 'closed'"
            (stateChanged)="contentsOpen.set($event === 'open')"
          >
            <!-- The panel is drawn outside the page, so it carries the theme itself. -->
            <hlm-sheet-content
              *hlmSheetPortal
              class="gap-0 overflow-y-auto"
              [attr.data-reader-theme]="settings().theme"
              [style.--background]="colours().background"
              [style.--foreground]="colours().text"
              [style.--popover]="colours().background"
              [style.--popover-foreground]="colours().text"
            >
              <hlm-sheet-header>
                <h2 hlmSheetTitle>Contents</h2>
              </hlm-sheet-header>
              <nav class="px-2 pb-4">
                <ng-container
                  [ngTemplateOutlet]="entries"
                  [ngTemplateOutletContext]="{ $implicit: contents() }"
                />
              </nav>
            </hlm-sheet-content>
          </hlm-sheet>
        }
        <button
          hlmBtn
          variant="outline"
          size="icon-sm"
          type="button"
          title="Scrolled layout"
          [attr.aria-pressed]="settings().layout === 'scrolled'"
          (click)="toggleLayout()"
        >
          <ng-icon name="lucideScrollText" />
          <span class="sr-only">Scrolled layout</span>
        </button>
        <button
          hlmBtn
          variant="outline"
          size="icon-sm"
          type="button"
          title="Smaller text"
          [disabled]="settings().fontSize <= fontSize.min"
          (click)="changeFontSize(-fontSize.step)"
        >
          <ng-icon name="lucideAArrowDown" />
          <span class="sr-only">Smaller text</span>
        </button>
        <button
          hlmBtn
          variant="outline"
          size="icon-sm"
          type="button"
          title="Larger text"
          [disabled]="settings().fontSize >= fontSize.max"
          (click)="changeFontSize(fontSize.step)"
        >
          <ng-icon name="lucideAArrowUp" />
          <span class="sr-only">Larger text</span>
        </button>
        <button
          hlmBtn
          variant="outline"
          size="icon-sm"
          type="button"
          [title]="themeLabel()"
          (click)="nextTheme()"
        >
          <ng-icon name="lucidePalette" />
          <span class="sr-only">{{ themeLabel() }}</span>
        </button>
        <div class="ms-auto flex gap-2">
          <button
            hlmBtn
            variant="outline"
            size="icon-sm"
            type="button"
            title="Previous page"
            (click)="previous()"
          >
            <ng-icon name="lucideChevronLeft" />
            <span class="sr-only">Previous page</span>
          </button>
          <button
            hlmBtn
            variant="outline"
            size="icon-sm"
            type="button"
            title="Next page"
            (click)="next()"
          >
            <ng-icon name="lucideChevronRight" />
            <span class="sr-only">Next page</span>
          </button>
        </div>
      }
    </header>
    @if (status() === 'failed') {
      <p class="text-destructive p-4 text-sm" role="alert">{{ couldNotShow }}</p>
    }
    <!-- An entry's nested entries are listed inside it, indented, as deep as the book goes. -->
    <ng-template #entries let-list>
      <ul>
        @for (entry of list; track $index) {
          <li>
            <button
              hlmBtn
              variant="ghost"
              size="sm"
              type="button"
              class="h-auto w-full justify-start py-1.5 text-start whitespace-normal"
              (click)="goTo(entry)"
            >
              {{ entry.label }}
            </button>
            @if (entry.children.length > 0) {
              <div class="ps-4">
                <ng-container
                  [ngTemplateOutlet]="entries"
                  [ngTemplateOutletContext]="{ $implicit: entry.children }"
                />
              </div>
            }
          </li>
        }
      </ul>
    </ng-template>
    <div #book class="relative min-h-0 flex-1" [class.hidden]="status() === 'failed'"></div>
    @if (status() !== 'failed') {
      <!-- A fixed height: the navigator loses the place within a chapter when the book is resized, which a readout
           that grew with its first text would do just after opening at a saved place. -->
      <footer
        class="text-muted-foreground flex h-9 shrink-0 items-center gap-4 border-t px-4 text-sm"
      >
        <span class="min-w-0 flex-1 truncate" data-reader-chapter>{{ place()?.chapterTitle }}</span>
        <span class="tabular-nums" data-reader-progress>{{ percent() }}</span>
      </footer>
    }
  `,
  // The engine's frames are made outside this component's template, so the rule has to reach past its encapsulation.
  // The navigator positions them but leaves their size to the page.
  // The reader's own colours follow the book's theme, not the app's, on the page and on the contents panel, which is
  // drawn outside it. Each is marked with the theme: the app's stylesheet gives light and dark their colours, and the
  // rule here gives sepia its own. The background and text are the very colours the book is drawn in.
  styles: `
    :host ::ng-deep .readium-navigator-iframe {
      width: 100%;
      height: 100%;
      border-width: 0;
    }

    :host([data-reader-theme='sepia']),
    [data-reader-theme='sepia'] {
      color-scheme: light;
      --muted: #e9dcc0;
      --muted-foreground: #7d6650;
      --destructive: oklch(0.577 0.245 27.325);
      --border: #d8c8a8;
      --input: #d8c8a8;
      --ring: #b39d78;
    }
  `,
})
export class ReaderPage {
  private readonly loadEngine = inject(READER_ENGINE);
  private readonly route = inject(ActivatedRoute);
  private readonly places = inject(ReaderPlaceStore);
  private readonly settingsStore = inject(ReaderSettingsStore);
  private readonly book = viewChild.required<ElementRef<HTMLElement>>('book');

  protected readonly couldNotShow = couldNotShow;
  protected readonly workId = signal('');
  private itemId = '';

  // The engine reports from outside Angular, so what the page shows is kept in signals.
  protected readonly status = signal<Status>('opening');
  protected readonly place = signal<ReaderPlace | null>(null);
  /** The book's table of contents, empty until the book has opened and for a book without one. */
  protected readonly contents = signal<readonly ReaderContentsEntry[]>([]);
  protected readonly contentsOpen = signal(false);
  protected readonly percent = computed(() => {
    const place = this.place();
    return place ? `${Math.round(place.fraction * 100)}%` : '';
  });
  /** How the book is shown: what the engine was last given, or is given as soon as the book has opened. */
  protected readonly settings = signal<ReaderSettings>(this.settingsStore.read());
  protected readonly fontSize = readerFontSize;
  protected readonly colours = computed(() => readerThemeColours[this.settings().theme]);
  protected readonly themeLabel = computed(() => `Theme: ${this.settings().theme}`);

  private engine: ReaderEngine | null = null;
  /** Counts the books this page has been asked to show, so a book that is still opening can tell it was replaced. */
  private opening = 0;

  constructor() {
    const destroyRef = inject(DestroyRef);
    // The router keeps this page when the address moves from one book to another, so the page follows the address
    // rather than reading it once.
    afterNextRender(() =>
      this.route.paramMap
        .pipe(takeUntilDestroyed(destroyRef))
        .subscribe((params) => this.show(params.get('workId') ?? '', params.get('itemId') ?? '')),
    );
    destroyRef.onDestroy(() => this.leave());
  }

  /** Shows the item's book in place of the one on screen, if any. */
  private show(workId: string, itemId: string): void {
    this.leave();
    this.workId.set(workId);
    this.itemId = itemId;
    this.place.set(null);
    this.contents.set([]);
    this.contentsOpen.set(false);
    this.settings.set(this.settingsStore.read());
    this.status.set('opening');
    void this.open(this.opening);
  }

  /** Keeps the place of the book on screen and takes the book down. */
  private leave(): void {
    this.opening++;
    this.places.flush();
    this.close();
  }

  private async open(opening: number): Promise<void> {
    const replaced = () => opening !== this.opening;
    try {
      const engine = await this.loadEngine();
      if (replaced()) {
        return;
      }
      this.engine = engine;
      const settings = this.settings();
      const { contents } = await engine.open(
        this.book().nativeElement,
        publicationManifestUrl(this.itemId),
        {
          place: this.places.read(this.itemId),
          settings,
          onPlace: (place) => replaced() || this.moved(place),
          onError: () => replaced() || this.fail(),
          onTap: (zone) => replaced() || this.tapped(zone),
          onKey: (key) => replaced() || this.turn(key),
        },
      );
      if (!replaced() && this.status() === 'opening') {
        this.contents.set(contents);
        this.status.set('open');
        // A setting changed while the book was opening was kept but not yet given to the engine.
        if (this.settings() !== settings) {
          this.applySettings();
        }
      }
    } catch {
      if (!replaced()) {
        this.fail();
      }
    }
  }

  private moved(place: ReaderPlace): void {
    this.place.set(place);
    this.places.save(this.itemId, place);
  }

  protected next(): void {
    this.move((engine) => engine.next());
  }

  protected previous(): void {
    this.move((engine) => engine.previous());
  }

  /** Goes to a table of contents entry and puts the contents away. */
  protected goTo(entry: ReaderContentsEntry): void {
    this.contentsOpen.set(false);
    this.move((engine) => engine.goTo(entry));
  }

  protected toggleLayout(): void {
    const { layout } = this.settings();
    this.change({ layout: layout === 'paginated' ? 'scrolled' : 'paginated' });
  }

  /** Makes the text larger or smaller by `by` percent, within the smallest and largest size. */
  protected changeFontSize(by: number): void {
    const size = this.settings().fontSize + by;
    if (size >= readerFontSize.min && size <= readerFontSize.max) {
      this.change({ fontSize: size });
    }
  }

  protected nextTheme(): void {
    const { theme } = this.settings();
    this.change({ theme: readerThemes[(readerThemes.indexOf(theme) + 1) % readerThemes.length] });
  }

  /** Keeps a changed setting for every book and shows this one with it. */
  private change(changed: Partial<ReaderSettings>): void {
    this.settings.update((settings) => ({ ...settings, ...changed }));
    this.settingsStore.save(this.settings());
    // While the book is opening the engine has not taken the settings it was opened with; they are applied once it has.
    if (this.status() === 'open') {
      this.applySettings();
    }
  }

  private applySettings(): void {
    const settings = this.settings();
    this.move((engine) => engine.apply(settings));
  }

  // A tap in the middle of the book does nothing.
  private tapped(zone: ReaderTapZone): void {
    if (zone !== 'middle') {
      this.turn(zone);
    }
  }

  // Taps and arrow keys name a side of the book rather than a direction, so a book read right to left turns the way
  // its reader expects.
  private turn(side: ReaderArrowKey): void {
    this.move((engine) => (side === 'left' ? engine.goLeft() : engine.goRight()));
  }

  private move(step: (engine: ReaderEngine) => Promise<void>): void {
    if (this.engine) {
      step(this.engine).catch(() => this.fail());
    }
  }

  /** Shows the error in place of the book and takes the book down. */
  private fail(): void {
    this.status.set('failed');
    this.close();
  }

  private close(): void {
    const engine = this.engine;
    this.engine = null;
    void engine?.close().catch(() => undefined);
  }
}
