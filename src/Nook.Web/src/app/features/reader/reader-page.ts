import {
  Component,
  DOCUMENT,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  effect,
  inject,
  isDevMode,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgTemplateOutlet } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideAArrowDown,
  lucideAArrowUp,
  lucideALargeSmall,
  lucideArrowLeft,
  lucideChevronLeft,
  lucideChevronRight,
  lucideListTree,
} from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmSheetImports } from '@spartan-ng/helm/sheet';
import { HlmToggleGroupImports } from '@spartan-ng/helm/toggle-group';
import { publicationManifestUrl } from '../../api/publications-api';
import {
  READER_ENGINE,
  ReaderArrowKey,
  ReaderContentsEntry,
  ReaderEngine,
  ReaderLayout,
  ReaderLocator,
  ReaderPlace,
  ReaderSettings,
  ReaderTapZone,
  ReaderTheme,
  readerFontFamilies,
  readerFonts,
  readerLayouts,
  readerLineHeights,
  readerMargins,
  readerThemeColours,
} from './reader-engine';
import { ReaderPlaceStore } from './reader-place-store';
import { SystemTheme } from '../../core/system-theme';
import {
  KeptReaderSettings,
  ReaderSettingsStore,
  readerFontSize,
  readerThemeChoices,
} from './reader-settings-store';
import { ReaderThemed } from './reader-themed';

const couldNotShow = 'This book could not be shown.';

/** Where the page stands with the book. */
type Status = 'opening' | 'open' | 'failed';

/**
 * The reader's state as checks run in a browser read it, set on the window in dev mode only. Besides these, it carries
 * whatever the engine tells about itself (see `ReaderEngine.inspect`).
 */
export interface ReaderBrowserState {
  readonly [detail: string]: unknown;
  /** Whether the book has opened and its place has been reported. */
  readonly ready: boolean;
  readonly locator: ReaderLocator | null;
  readonly fraction: number | null;
  readonly flow: ReaderLayout;
  /** The labels of the table of contents, nested entries in order after the one they are under. */
  readonly tocLabels: readonly string[];
  readonly fontSize: number;
  readonly theme: ReaderTheme;
  /** How many places the engine has reported. */
  readonly relocateCount: number;
  /** The failures seen, as text. */
  readonly errors: readonly string[];
  /** Goes to the place nearest `fraction` of the way through the book. */
  readonly goToFraction: (fraction: number) => Promise<void>;
}

declare global {
  interface Window {
    __nookReader?: ReaderBrowserState;
  }
}

/** The display settings that are one of a few named choices. */
type ReaderChoiceSetting = 'theme' | 'font' | 'lineHeight' | 'margins' | 'layout';

/** One of the choices a display setting offers. */
interface ReaderChoiceOption<V> {
  readonly value: V;
  readonly label: string;
  /** The font the label is set in. */
  readonly font?: string;
}

/** A display setting that is one of a few named choices, as the settings panel lists it. */
interface ReaderChoice<K extends ReaderChoiceSetting = ReaderChoiceSetting> {
  readonly setting: K;
  readonly label: string;
  /** The id of the control the setting is chosen with. */
  readonly id: string;
  readonly options: readonly ReaderChoiceOption<KeptReaderSettings[K]>[];
  /** What the reader calls a choice, or nothing for a value that is not one of them. */
  readonly labelOf: (value: unknown) => string;
}

/**
 * A setting with the choices it offers: every one of `values`, the list the kept setting is checked against, in that
 * order and under its name in `labels`.
 */
function choiceOf<K extends ReaderChoiceSetting>(
  setting: K,
  label: string,
  id: string,
  values: readonly KeptReaderSettings[K][],
  labels: Record<KeptReaderSettings[K], string>,
  fonts: Partial<Record<KeptReaderSettings[K], string>> = {},
): ReaderChoice<K> {
  const options = values.map((value) => ({ value, label: labels[value], font: fonts[value] }));
  const labelOf = (value: unknown) => options.find((option) => option.value === value)?.label ?? '';
  return { setting, label, id, options, labelOf };
}

/** The settings chosen from a list, with what the reader calls each choice. */
const choices = {
  theme: choiceOf('theme', 'Theme', 'reader-theme', readerThemeChoices, {
    auto: 'Auto',
    light: 'Light',
    dark: 'Dark',
    sepia: 'Sepia',
  }),
  font: choiceOf(
    'font',
    'Font',
    'reader-font',
    readerFonts,
    { book: 'Book', serif: 'Serif', sans: 'Sans' },
    readerFontFamilies,
  ),
  lineHeight: choiceOf('lineHeight', 'Line height', 'reader-line-height', readerLineHeights, {
    book: 'Book',
    tight: 'Tight',
    medium: 'Medium',
    loose: 'Loose',
  }),
  margins: choiceOf('margins', 'Margins', 'reader-margins', readerMargins, {
    narrow: 'Narrow',
    normal: 'Normal',
    wide: 'Wide',
  }),
  layout: choiceOf('layout', 'Layout', 'reader-flow', readerLayouts, {
    paginated: 'Paginated',
    scrolled: 'Scrolled',
  }),
};

/** From this width up the display settings come in from the side; below it, from the bottom. */
const wideScreen = '(min-width: 640px)';

/**
 * One book, full-screen: a toolbar with the way back to its work, the book's table of contents, the display settings
 * and the page turns, the book itself, which the reader engine renders and moves through, and a readout of how far
 * through the book and which chapter the reader is in.
 * The display settings (theme, font, text size, line height, margins and layout) are in a panel of their own.
 * The book opens where it was last left in this browser, and the place is kept as the reader moves. How books are shown
 * is kept in this browser too, the same for every book, and the reader's own colours follow the book's theme. A
 * theme left on auto is the app's light or dark, and changes when the app's does.
 */
@Component({
  selector: 'app-reader-page',
  imports: [
    HlmButtonImports,
    HlmSelectImports,
    HlmSheetImports,
    HlmToggleGroupImports,
    NgIcon,
    NgTemplateOutlet,
    ReaderThemed,
    RouterLink,
  ],
  providers: [
    provideIcons({
      lucideAArrowDown,
      lucideAArrowUp,
      lucideALargeSmall,
      lucideArrowLeft,
      lucideChevronLeft,
      lucideChevronRight,
      lucideListTree,
    }),
  ],
  host: {
    class: 'bg-background text-foreground flex h-dvh flex-col',
    '[attr.data-reader-theme]': 'theme()',
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
            data-testid="reader-toc-open"
            (click)="contentsOpen.set(true)"
          >
            <ng-icon name="lucideListTree" />
            <span class="sr-only">Contents</span>
          </button>
          <hlm-sheet
            side="right"
            [state]="contentsOpen() ? 'open' : 'closed'"
            (stateChanged)="contentsOpen.set($event === 'open')"
          >
            <!-- The panel is drawn outside the page, so it carries the theme itself. -->
            <hlm-sheet-content
              *hlmSheetPortal
              class="gap-0 overflow-y-auto"
              [appReaderThemed]="theme()"
            >
              <hlm-sheet-header>
                <h2 hlmSheetTitle class="leading-8">Contents</h2>
              </hlm-sheet-header>
              <nav class="px-2 pb-4" data-testid="reader-toc">
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
          title="Display settings"
          data-testid="reader-settings-open"
          (click)="openSettings()"
        >
          <ng-icon name="lucideALargeSmall" />
          <span class="sr-only">Display settings</span>
        </button>
        <hlm-sheet
          [side]="settingsSide()"
          [state]="settingsOpen() ? 'open' : 'closed'"
          (stateChanged)="settingsOpen.set($event === 'open')"
        >
          <hlm-sheet-content
            *hlmSheetPortal
            class="max-h-dvh gap-0 overflow-y-auto"
            data-testid="reader-settings"
            [appReaderThemed]="theme()"
          >
            <hlm-sheet-header>
              <h2 hlmSheetTitle class="leading-8">Display</h2>
            </hlm-sheet-header>
            <div class="flex flex-col gap-4 px-4 pb-6">
              <ng-container
                [ngTemplateOutlet]="choice"
                [ngTemplateOutletContext]="{ $implicit: choices.theme }"
              />
              <ng-container
                [ngTemplateOutlet]="choice"
                [ngTemplateOutletContext]="{ $implicit: choices.font }"
              />
              <div class="flex items-center justify-between gap-4">
                <span id="reader-font-size-label">Text size</span>
                <div
                  class="flex items-center gap-2"
                  role="group"
                  aria-labelledby="reader-font-size-label"
                >
                  <button
                    hlmBtn
                    variant="outline"
                    size="icon-sm"
                    type="button"
                    title="Smaller text"
                    data-testid="reader-font-smaller"
                    [disabled]="settings().fontSize <= fontSize.min"
                    (click)="changeFontSize(-fontSize.step)"
                  >
                    <ng-icon name="lucideAArrowDown" />
                    <span class="sr-only">Smaller text</span>
                  </button>
                  <span
                    class="w-12 text-center tabular-nums"
                    aria-live="polite"
                    data-testid="reader-font-size"
                    >{{ settings().fontSize }}%</span
                  >
                  <button
                    hlmBtn
                    variant="outline"
                    size="icon-sm"
                    type="button"
                    title="Larger text"
                    data-testid="reader-font-larger"
                    [disabled]="settings().fontSize >= fontSize.max"
                    (click)="changeFontSize(fontSize.step)"
                  >
                    <ng-icon name="lucideAArrowUp" />
                    <span class="sr-only">Larger text</span>
                  </button>
                </div>
              </div>
              <ng-container
                [ngTemplateOutlet]="choice"
                [ngTemplateOutletContext]="{ $implicit: choices.lineHeight }"
              />
              <ng-container
                [ngTemplateOutlet]="choice"
                [ngTemplateOutletContext]="{ $implicit: choices.margins }"
              />
              <div class="flex items-center justify-between gap-4">
                <span>{{ choices.layout.label }}</span>
                <div
                  hlmToggleGroup
                  type="single"
                  variant="outline"
                  size="sm"
                  [attr.aria-label]="choices.layout.label"
                  [attr.data-testid]="choices.layout.id"
                  [nullable]="false"
                  [value]="settings().layout"
                  (valueChange)="choose(choices.layout, $event)"
                >
                  @for (option of choices.layout.options; track option.value) {
                    <button hlmToggleGroupItem type="button" [value]="option.value">
                      {{ option.label }}
                    </button>
                  }
                </div>
              </div>
            </div>
          </hlm-sheet-content>
        </hlm-sheet>
        <div class="ms-auto flex gap-2">
          <button
            hlmBtn
            variant="outline"
            size="icon-sm"
            type="button"
            title="Previous page"
            data-testid="reader-prev"
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
            data-testid="reader-next"
            (click)="next()"
          >
            <ng-icon name="lucideChevronRight" />
            <span class="sr-only">Next page</span>
          </button>
        </div>
      }
    </header>
    @if (status() === 'failed') {
      <p class="text-destructive p-4 text-sm" role="alert" data-testid="reader-error">
        {{ couldNotShow }}
      </p>
    }
    <!-- A display setting chosen from a list: its name, and a select showing the choice in use. -->
    <ng-template #choice let-row>
      <div class="flex items-center justify-between gap-4">
        <label [for]="row.id">{{ row.label }}</label>
        <hlm-select
          [value]="chosen(row)"
          [itemToString]="row.labelOf"
          (valueChange)="choose(row, $event)"
        >
          <hlm-select-trigger size="sm" class="w-32" [buttonId]="row.id">
            <hlm-select-value />
          </hlm-select-trigger>
          <!-- The list is drawn outside the page, like the panels, so it carries the theme itself. -->
          <hlm-select-content *hlmSelectPortal [appReaderThemed]="theme()">
            <hlm-select-group>
              @for (option of row.options; track option.value) {
                <hlm-select-item [value]="option.value" [style.font-family]="option.font">
                  {{ option.label }}
                </hlm-select-item>
              }
            </hlm-select-group>
          </hlm-select-content>
        </hlm-select>
      </div>
    </ng-template>
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
    <div
      #book
      class="relative min-h-0 flex-1"
      data-testid="reader-view"
      [class.hidden]="status() === 'failed'"
    ></div>
    @if (status() !== 'failed') {
      <!-- A fixed height: the navigator loses the place within a chapter when the book is resized, which a readout
           that grew with its first text would do just after opening at a saved place. -->
      <footer
        class="text-muted-foreground flex h-9 shrink-0 items-center gap-4 border-t px-4 text-sm"
        data-testid="reader-status"
      >
        <span class="min-w-0 flex-1 truncate" data-reader-chapter>{{ place()?.chapterTitle }}</span>
        <span class="tabular-nums" data-reader-progress>{{ percent() }}</span>
      </footer>
    }
  `,
  // The engine's frames are made outside this component's template, so the rule has to reach past its encapsulation.
  // The navigator positions them but leaves their size to the page.
  // The reader's own colours follow the book's theme, not the app's, on the page and on its panels and lists, which are
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
      --accent: #e9dcc0;
      --accent-foreground: #5b4636;
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
  private readonly document = inject(DOCUMENT);
  private readonly systemTheme = inject(SystemTheme);
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
  /** How books are shown, as chosen in the display settings. */
  protected readonly settings = signal<KeptReaderSettings>(this.settingsStore.read());
  /** The theme the book is in: the one chosen, or on auto the app's light or dark. */
  protected readonly theme = computed<ReaderTheme>(() => {
    const { theme } = this.settings();
    return theme !== 'auto' ? theme : this.systemTheme.dark() ? 'dark' : 'light';
  });
  /** How the book is shown: what the engine was last given, or is given as soon as the book has opened. */
  private readonly shown = computed<ReaderSettings>(() => ({
    ...this.settings(),
    theme: this.theme(),
  }));
  protected readonly fontSize = readerFontSize;
  protected readonly colours = computed(() => readerThemeColours[this.theme()]);
  protected readonly choices = choices;
  protected readonly settingsOpen = signal(false);
  /** Which edge the display settings come in from, chosen each time they are opened. */
  protected readonly settingsSide = signal<'bottom' | 'right'>('bottom');

  private engine: ReaderEngine | null = null;
  /** Whether the reader's state is set on the window for checks run in a browser. */
  private readonly exposed = isDevMode();
  /** How many places the engine has reported for the book on screen, for the browser checks. */
  private relocateCount = 0;
  /** The failures seen with the book on screen, as text, for the browser checks. */
  private errors: string[] = [];
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
    // A book on the auto theme changes colours with the app. A theme chosen in the settings is applied as it is chosen.
    effect(() => {
      this.systemTheme.dark();
      untracked(() => {
        if (this.settings().theme !== 'auto') {
          return;
        }
        // The page takes the new colours whatever the book is doing; a book still opening is given them once it has.
        this.expose();
        if (this.status() === 'open') {
          this.applySettings();
        }
      });
    });
  }

  /** Shows the item's book in place of the one on screen, if any. */
  private show(workId: string, itemId: string): void {
    this.leave();
    this.workId.set(workId);
    this.itemId = itemId;
    this.place.set(null);
    this.contents.set([]);
    this.contentsOpen.set(false);
    this.settingsOpen.set(false);
    this.settings.set(this.settingsStore.read());
    this.status.set('opening');
    this.relocateCount = 0;
    this.errors = [];
    this.expose();
    void this.open(this.opening);
  }

  /** Keeps the place of the book on screen and takes the book down. */
  private leave(): void {
    this.opening++;
    this.places.flush();
    this.close();
    if (this.exposed) {
      delete window.__nookReader;
    }
  }

  private async open(opening: number): Promise<void> {
    const replaced = () => opening !== this.opening;
    try {
      const engine = await this.loadEngine();
      if (replaced()) {
        return;
      }
      this.engine = engine;
      const settings = this.shown();
      const { contents } = await engine.open(
        this.book().nativeElement,
        publicationManifestUrl(this.itemId),
        {
          place: this.places.read(this.itemId),
          settings,
          onPlace: (place) => replaced() || this.moved(place),
          onError: (error) => replaced() || this.fail(error),
          onTap: (zone) => replaced() || this.tapped(zone),
          onKey: (key) => replaced() || this.turn(key),
        },
      );
      if (!replaced() && this.status() === 'opening') {
        this.contents.set(contents);
        this.status.set('open');
        this.expose();
        // A setting changed while the book was opening was kept but not yet given to the engine.
        if (this.shown() !== settings) {
          this.applySettings();
        }
      }
    } catch (error) {
      if (!replaced()) {
        this.fail(error);
      }
    }
  }

  private moved(place: ReaderPlace): void {
    this.place.set(place);
    this.places.save(this.itemId, place);
    this.relocateCount++;
    this.expose();
  }

  protected next(): void {
    void this.move((engine) => engine.next());
  }

  protected previous(): void {
    void this.move((engine) => engine.previous());
  }

  /** Goes to a table of contents entry and puts the contents away. */
  protected goTo(entry: ReaderContentsEntry): void {
    this.contentsOpen.set(false);
    void this.move((engine) => engine.goTo(entry));
  }

  /** Opens the display settings beside the book on a wide screen and under it on a narrow one. */
  protected openSettings(): void {
    const wide = this.document.defaultView?.matchMedia?.(wideScreen).matches ?? false;
    this.settingsSide.set(wide ? 'right' : 'bottom');
    this.settingsOpen.set(true);
  }

  /** The choice in use for a setting. */
  protected chosen(choice: ReaderChoice): string {
    return this.settings()[choice.setting];
  }

  /** Takes the choice made for a setting, when it is one the setting offers and not the one already in use. */
  protected choose<K extends ReaderChoiceSetting>(choice: ReaderChoice<K>, value: unknown): void {
    const option = choice.options.find((offered) => offered.value === value);
    if (option && option.value !== this.settings()[choice.setting]) {
      this.change({ [choice.setting]: option.value });
    }
  }

  /** Makes the text larger or smaller by `by` percent, within the smallest and largest size. */
  protected changeFontSize(by: number): void {
    const size = this.settings().fontSize + by;
    if (size >= readerFontSize.min && size <= readerFontSize.max) {
      this.change({ fontSize: size });
    }
  }

  /** Keeps a changed setting for every book and shows this one with it. */
  private change(changed: Partial<KeptReaderSettings>): void {
    this.settings.update((settings) => ({ ...settings, ...changed }));
    this.settingsStore.save(this.settings());
    this.expose();
    // While the book is opening the engine has not taken the settings it was opened with; they are applied once it has.
    if (this.status() === 'open') {
      this.applySettings();
    }
  }

  private applySettings(): void {
    const settings = this.shown();
    void this.move((engine) => engine.apply(settings));
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
    void this.move((engine) => (side === 'left' ? engine.goLeft() : engine.goRight()));
  }

  /** Runs a step on the book on screen. A step that fails after its book was left or replaced is let go. */
  private async move(step: (engine: ReaderEngine) => Promise<void>): Promise<void> {
    const engine = this.engine;
    if (!engine) {
      return;
    }
    const opening = this.opening;
    try {
      await step(engine);
    } catch (error) {
      if (opening === this.opening) {
        this.fail(error);
      }
    }
  }

  /** Shows the error in place of the book and takes the book down. */
  private fail(error: unknown): void {
    this.status.set('failed');
    this.errors.push(String(error));
    this.close();
    this.expose();
  }

  /**
   * In dev mode, sets the reader's state on the window as it stands now, for checks run in a browser to read. It is
   * set again on every change they look at: a place reported, the book opened, a setting changed, a failure.
   */
  private expose(): void {
    if (!this.exposed) {
      return;
    }
    const place = this.place();
    const settings = this.settings();
    window.__nookReader = {
      ...this.engine?.inspect(),
      ready: this.status() === 'open' && place !== null,
      locator: place?.locator ?? null,
      fraction: place?.fraction ?? null,
      flow: settings.layout,
      tocLabels: labelsOf(this.contents()),
      fontSize: settings.fontSize,
      theme: this.theme(),
      relocateCount: this.relocateCount,
      errors: [...this.errors],
      goToFraction: (fraction) => this.move((engine) => engine.goToFraction(fraction)),
    };
  }

  private close(): void {
    const engine = this.engine;
    this.engine = null;
    void engine?.close().catch(() => undefined);
  }
}

/** The labels of `entries` and of the entries under each, in the order the contents list them. */
function labelsOf(entries: readonly ReaderContentsEntry[]): string[] {
  return entries.flatMap((entry) => [entry.label, ...labelsOf(entry.children)]);
}
