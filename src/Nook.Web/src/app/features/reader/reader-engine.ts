import { InjectionToken } from '@angular/core';

/** A value that survives a round trip through `JSON.stringify` and `JSON.parse`. */
export type JsonValue =
  string | number | boolean | null | JsonValue[] | { [key: string]: JsonValue };

/**
 * A place in a book as the engine's own format records it (for Readium, its Locator), as JSON. Only the engine reads
 * what is inside it beyond `href`, the book file it points into.
 */
export interface ReaderLocator {
  readonly href: string;
  readonly [key: string]: JsonValue;
}

/** Where the reader is, as reported on every position change. */
export interface ReaderPlace {
  /** The place itself: what is kept to open the book there again. */
  readonly locator: ReaderLocator;
  /** How far through the whole book the place is, from 0 to 1. */
  readonly fraction: number;
  /** The title of the chapter the place is in, or null when the book gives none. */
  readonly chapterTitle: string | null;
}

/** Whether a book flows into pages turned one at a time or into one long column per chapter. */
export type ReaderLayout = 'paginated' | 'scrolled';

/** The colours the book and the reader around it are shown in. */
export type ReaderTheme = 'light' | 'dark' | 'sepia';

/** How a book is shown. The same settings apply to every book. */
export interface ReaderSettings {
  readonly layout: ReaderLayout;
  /** The text size as a percentage of the book's own, from 70 to 250. */
  readonly fontSize: number;
  readonly theme: ReaderTheme;
}

/** One entry in a book's table of contents, with the entries nested under it. */
export interface ReaderContentsEntry {
  readonly label: string;
  /** The book file the entry points to, with its fragment when it points inside the file. */
  readonly href: string;
  readonly children: readonly ReaderContentsEntry[];
}

/** Which third of the book a tap that was not on a link landed in. */
export type ReaderTapZone = 'left' | 'middle' | 'right';

/** An arrow key pressed while reading. */
export type ReaderArrowKey = 'left' | 'right';

/** What a book is opened with, and where the engine reports back to while it is open. */
export interface ReaderOpenOptions {
  /** Where to open the book. Without one, or with one the book does not contain, the book opens at its start. */
  readonly place?: ReaderLocator;
  readonly settings: ReaderSettings;
  /** Called on every position change. */
  readonly onPlace: (place: ReaderPlace) => void;
  /** Called when the engine fails after the book has opened. */
  readonly onError: (error: unknown) => void;
  /** Called for a tap in the book that was not on a link. The engine follows a link tap itself. */
  readonly onTap: (zone: ReaderTapZone) => void;
  /**
   * Called for every left or right arrow key, pressed on the page or inside the book, except while a text field has
   * focus. The page needs no key listener of its own.
   */
  readonly onKey: (key: ReaderArrowKey) => void;
}

/** What is known about a book once it has opened. */
export interface ReaderOpened {
  /** The book's table of contents, empty when it has none. */
  readonly contents: readonly ReaderContentsEntry[];
}

/**
 * Shows a book and moves through it. The reader page reaches the book only through this, so it can be tested without
 * a browser layout engine. Every method but `open` acts on the book `open` opened, and each resolves when its move or
 * change is done.
 */
export interface ReaderEngine {
  /** Renders the book whose Readium manifest is at `manifestUrl` into `host`. Rejects when the book cannot be opened. */
  open(host: HTMLElement, manifestUrl: string, options: ReaderOpenOptions): Promise<ReaderOpened>;
  /** Moves forward a page, or about a screen in scrolled layout. */
  next(): Promise<void>;
  /** Moves back a page, or about a screen in scrolled layout. */
  previous(): Promise<void>;
  /** Moves toward the left edge of the book: back in a book read left to right. */
  goLeft(): Promise<void>;
  /** Moves toward the right edge of the book: forward in a book read left to right. */
  goRight(): Promise<void>;
  /** Goes to a table of contents entry. */
  goTo(entry: ReaderContentsEntry): Promise<void>;
  /** Goes to the place nearest `fraction`, from 0 to 1, of the way through the book. */
  goToFraction(fraction: number): Promise<void>;
  /** Shows the book with new settings, keeping the place. */
  apply(settings: ReaderSettings): Promise<void>;
  /** Takes the book down and stops reporting. */
  close(): Promise<void>;
}

/** Loads an engine, so its code is fetched only when a book is opened. */
export type ReaderEngineLoader = () => Promise<ReaderEngine>;

/** The function the reader page calls to get an engine: Readium's, loaded on first use. */
export const READER_ENGINE = new InjectionToken<ReaderEngineLoader>('READER_ENGINE', {
  providedIn: 'root',
  factory: () => () => import('./readium-engine').then(({ ReadiumEngine }) => new ReadiumEngine()),
});
