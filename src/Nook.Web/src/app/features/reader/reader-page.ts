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
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { publicationManifestUrl } from '../../api/publications-api';
import {
  READER_ENGINE,
  ReaderArrowKey,
  ReaderEngine,
  ReaderPlace,
  ReaderSettings,
  ReaderTapZone,
} from './reader-engine';
import { ReaderPlaceStore } from './reader-place-store';

/** How the book is shown until the reader has settings of its own. */
const defaultSettings: ReaderSettings = { layout: 'paginated', fontSize: 100, theme: 'light' };

const couldNotShow = 'This book could not be shown.';

/** Where the page stands with the book. */
type Status = 'opening' | 'open' | 'failed';

/**
 * One book, full-screen: a toolbar with the way back to its work and the page turns, the book itself, which the reader
 * engine renders and moves through, and a readout of how far through the book and which chapter the reader is in.
 * The book opens where it was last left in this browser, and the place is kept as the reader moves.
 */
@Component({
  selector: 'app-reader-page',
  imports: [HlmButtonImports, RouterLink],
  host: { class: 'flex h-dvh flex-col' },
  template: `
    <header class="flex items-center gap-2 border-b p-2">
      <a hlmBtn variant="ghost" size="sm" [routerLink]="['/works', workId()]">Back to the work</a>
      @if (status() !== 'failed') {
        <div class="ms-auto flex gap-2">
          <button hlmBtn variant="outline" size="sm" type="button" (click)="previous()">
            Previous page
          </button>
          <button hlmBtn variant="outline" size="sm" type="button" (click)="next()">
            Next page
          </button>
        </div>
      }
    </header>
    @if (status() === 'failed') {
      <p class="text-destructive p-4 text-sm" role="alert">{{ couldNotShow }}</p>
    }
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
  styles: `
    :host ::ng-deep .readium-navigator-iframe {
      width: 100%;
      height: 100%;
      border-width: 0;
    }
  `,
})
export class ReaderPage {
  private readonly loadEngine = inject(READER_ENGINE);
  private readonly route = inject(ActivatedRoute);
  private readonly places = inject(ReaderPlaceStore);
  private readonly book = viewChild.required<ElementRef<HTMLElement>>('book');

  protected readonly couldNotShow = couldNotShow;
  protected readonly workId = signal('');
  private itemId = '';

  // The engine reports from outside Angular, so what the page shows is kept in signals.
  protected readonly status = signal<Status>('opening');
  protected readonly place = signal<ReaderPlace | null>(null);
  protected readonly percent = computed(() => {
    const place = this.place();
    return place ? `${Math.round(place.fraction * 100)}%` : '';
  });

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
      await engine.open(this.book().nativeElement, publicationManifestUrl(this.itemId), {
        place: this.places.read(this.itemId),
        settings: defaultSettings,
        onPlace: (place) => replaced() || this.moved(place),
        onError: () => replaced() || this.fail(),
        onTap: (zone) => replaced() || this.tapped(zone),
        onKey: (key) => replaced() || this.turn(key),
      });
      if (!replaced() && this.status() === 'opening') {
        this.status.set('open');
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
