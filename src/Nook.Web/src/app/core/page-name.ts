import { Injectable, signal } from '@angular/core';

/**
 * The names of the works and people the pages have shown, which each page learns from the API and the shell shows in
 * its header and in the tab's title. A page that could not show one says what it shows in its place. Each name is kept
 * under the id it belongs to, so a name is never shown for another page, and an answer that arrives late for a page the
 * user has left cannot take the name of the page they are on.
 */
@Injectable({ providedIn: 'root' })
export class PageName {
  private readonly names = signal<ReadonlyMap<string, string>>(new Map());

  /** Records that the work or person `id` is called `name`. */
  set(id: string, name: string): void {
    this.names.update((names) => new Map(names).set(id, name));
  }

  /** Forgets every name, as when the user who was shown them signs out. */
  clear(): void {
    this.names.set(new Map());
  }

  /** The name recorded for `id`, or `null` when none is. */
  of(id: string): string | null {
    return this.names().get(id) ?? null;
  }
}
