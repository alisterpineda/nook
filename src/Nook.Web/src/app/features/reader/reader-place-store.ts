import { DOCUMENT, DestroyRef, Injectable, inject } from '@angular/core';
import { ReaderLocator, ReaderPlace } from './reader-engine';

/** The least time between two writes of one book's place while the reader keeps moving. */
const writeInterval = 1000;

/** The writes of one book's place: when the next may happen, and the place held until then. */
interface Writes {
  timer: ReturnType<typeof setTimeout>;
  held: ReaderLocator | null;
}

/**
 * Keeps the place each book was left at in the browser's storage, as the engine's Locator under
 * `nook.reader.position.<item id>`. The first change of place is written at once and later ones a second apart at
 * most, the latest held until its turn; whatever is held is written as soon as the page is hidden. Storage that fails
 * (private mode, a full quota) is ignored: the book then opens at its start.
 */
@Injectable({ providedIn: 'root' })
export class ReaderPlaceStore {
  private readonly document = inject(DOCUMENT);
  private readonly writes = new Map<string, Writes>();

  constructor() {
    const pageHidden = () => {
      if (this.document.visibilityState === 'hidden') {
        this.flush();
      }
    };
    this.document.addEventListener('visibilitychange', pageHidden);
    inject(DestroyRef).onDestroy(() => {
      this.document.removeEventListener('visibilitychange', pageHidden);
      this.flush();
    });
  }

  /** The place the item's book was left at, or undefined when none is kept or what is kept is not a Locator. */
  read(itemId: string): ReaderLocator | undefined {
    try {
      const value = this.storage()?.getItem(keyFor(itemId));
      if (value == null) {
        return undefined;
      }
      const locator: unknown = JSON.parse(value);
      return isLocator(locator) ? locator : undefined;
    } catch {
      return undefined;
    }
  }

  /** Keeps `place` as where the item's book was left, at once or within the second. */
  save(itemId: string, place: ReaderPlace): void {
    const writes = this.writes.get(itemId);
    if (writes) {
      writes.held = place.locator;
      return;
    }
    this.write(itemId, place.locator);
    this.wait(itemId);
  }

  /** Writes every place still held, so none is lost when the reader is left. */
  flush(): void {
    for (const [itemId, writes] of this.writes) {
      clearTimeout(writes.timer);
      if (writes.held) {
        this.write(itemId, writes.held);
      }
    }
    this.writes.clear();
  }

  // Holds the item's later places for a second, then writes the latest of them, if any, and holds again.
  private wait(itemId: string): void {
    const writes: Writes = {
      held: null,
      timer: setTimeout(() => {
        this.writes.delete(itemId);
        if (writes.held) {
          this.write(itemId, writes.held);
          this.wait(itemId);
        }
      }, writeInterval),
    };
    this.writes.set(itemId, writes);
  }

  private write(itemId: string, locator: ReaderLocator): void {
    try {
      this.storage()?.setItem(keyFor(itemId), JSON.stringify(locator));
    } catch {
      // The place is not kept, and the book opens at its start next time.
    }
  }

  // Reaching `localStorage` itself throws where the browser refuses storage to the page.
  private storage(): Storage | undefined {
    return this.document.defaultView?.localStorage;
  }
}

function keyFor(itemId: string): string {
  return `nook.reader.position.${itemId}`;
}

function isLocator(value: unknown): value is ReaderLocator {
  return (
    typeof value === 'object' &&
    value !== null &&
    !Array.isArray(value) &&
    typeof (value as { href?: unknown }).href === 'string'
  );
}
