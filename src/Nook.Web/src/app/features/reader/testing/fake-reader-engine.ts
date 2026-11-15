import { Provider } from '@angular/core';
import {
  READER_ENGINE,
  ReaderArrowKey,
  ReaderContentsEntry,
  ReaderEngine,
  ReaderEngineDetails,
  ReaderOpenOptions,
  ReaderOpened,
  ReaderPlace,
  ReaderSettings,
  ReaderTapZone,
} from '../reader-engine';

/** One call the fake engine received, in the order received. `open` is recorded in full in `opened`. */
export type FakeReaderEngineCall =
  | { readonly method: 'open'; readonly manifestUrl: string }
  | { readonly method: 'next' | 'previous' | 'goLeft' | 'goRight' | 'close' }
  | { readonly method: 'goTo'; readonly entry: ReaderContentsEntry }
  | { readonly method: 'goToFraction'; readonly fraction: number }
  | { readonly method: 'apply'; readonly settings: ReaderSettings };

/** What the fake engine was last asked to open. */
export interface FakeReaderEngineOpening {
  readonly host: HTMLElement;
  readonly manifestUrl: string;
  readonly options: ReaderOpenOptions;
}

/**
 * A reader engine for specs: it shows nothing, records every call, and lets the spec report places, errors, taps and
 * keys as a real engine would.
 */
export class FakeReaderEngine implements ReaderEngine {
  /** Every call received, in order. */
  readonly calls: FakeReaderEngineCall[] = [];
  /** The table of contents `open` resolves with. */
  contents: readonly ReaderContentsEntry[] = [];
  /** When set, `open` rejects with this instead of opening. */
  openError: unknown = undefined;
  /** What `inspect` gives back. */
  details: ReaderEngineDetails = {};
  /** When set, every later move or change settles as this does, instead of resolving at once. */
  settling: Promise<void> | null = null;

  private opening: FakeReaderEngineOpening | null = null;
  private closed = false;

  /** What the engine was last asked to open, or null when `open` has not been called. */
  get opened(): FakeReaderEngineOpening | null {
    return this.opening;
  }

  /** Whether `close` has been called since the last `open`. */
  get isClosed(): boolean {
    return this.closed;
  }

  open(host: HTMLElement, manifestUrl: string, options: ReaderOpenOptions): Promise<ReaderOpened> {
    this.calls.push({ method: 'open', manifestUrl });
    this.opening = { host, manifestUrl, options };
    this.closed = false;
    if (this.openError !== undefined) {
      return Promise.reject(this.openError);
    }
    return Promise.resolve({ contents: this.contents });
  }

  next(): Promise<void> {
    return this.record({ method: 'next' });
  }

  previous(): Promise<void> {
    return this.record({ method: 'previous' });
  }

  goLeft(): Promise<void> {
    return this.record({ method: 'goLeft' });
  }

  goRight(): Promise<void> {
    return this.record({ method: 'goRight' });
  }

  goTo(entry: ReaderContentsEntry): Promise<void> {
    return this.record({ method: 'goTo', entry });
  }

  goToFraction(fraction: number): Promise<void> {
    return this.record({ method: 'goToFraction', fraction });
  }

  apply(settings: ReaderSettings): Promise<void> {
    return this.record({ method: 'apply', settings });
  }

  close(): Promise<void> {
    this.closed = true;
    return this.record({ method: 'close' });
  }

  inspect(): ReaderEngineDetails {
    return this.details;
  }

  /** Reports a position change to the page, as the engine does on every move. */
  emitPlace(place: ReaderPlace): void {
    this.openOptions().onPlace(place);
  }

  /** Reports a failure after the book has opened. */
  emitError(error: unknown): void {
    this.openOptions().onError(error);
  }

  /** Reports a tap in the book that was not on a link. */
  emitTap(zone: ReaderTapZone): void {
    this.openOptions().onTap(zone);
  }

  /** Reports an arrow key. */
  emitKey(key: ReaderArrowKey): void {
    this.openOptions().onKey(key);
  }

  private record(call: FakeReaderEngineCall): Promise<void> {
    this.calls.push(call);
    return this.settling ?? Promise.resolve();
  }

  private openOptions(): ReaderOpenOptions {
    if (!this.opening) {
      throw new Error('The fake reader engine has not been opened');
    }
    return this.opening.options;
  }
}

/** Provides `engine` as the one the reader page loads. */
export function provideFakeReaderEngine(engine: FakeReaderEngine): Provider {
  return { provide: READER_ENGINE, useValue: () => Promise.resolve(engine) };
}
