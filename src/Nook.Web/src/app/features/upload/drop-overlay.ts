import { Component, HostListener, inject, input, signal } from '@angular/core';
import { Session } from '../../core/session';
import { AddBooks } from './add-books';

/** What a drag event carries, read by shape since not every environment has `DragEvent`. */
interface DragPayload {
  types?: readonly string[];
  files?: ArrayLike<File>;
}

/**
 * Catches files dropped anywhere on the window for someone who may upload, covering the window with an outline while
 * they are held over it and opening the "Add books" dialog on a drop. A drag that carries no files, or any drag for
 * someone who may not upload, is left to the browser.
 */
@Component({
  selector: 'app-drop-overlay',
  template: `
    @if (dragging()) {
      <div
        class="bg-background/80 border-primary pointer-events-none fixed inset-0 z-50 m-2 flex items-center justify-center rounded-xl border-2 border-dashed"
        role="presentation"
      >
        <p class="text-lg font-medium">Drop to add books</p>
      </div>
    }
  `,
  host: { class: 'pointer-events-none' },
})
export class DropOverlay {
  private readonly session = inject(Session);
  private readonly addBooks = inject(AddBooks);
  /** Drags that have entered an element and not yet left it, since leaving a child follows entering the next. */
  private depth = 0;

  /** The `section` query parameter of the area being browsed, which the dialog preselects. */
  readonly section = input<string | null>(null);

  protected readonly dragging = signal(false);

  @HostListener('window:dragenter', ['$event'])
  protected onEnter(event: Event): void {
    if (!this.watching(event)) {
      return;
    }
    this.depth++;
    this.dragging.set(true);
  }

  @HostListener('window:dragover', ['$event'])
  protected onOver(event: Event): void {
    if (this.watching(event)) {
      // Cancelling it is what lets the window take the drop.
      event.preventDefault();
    }
  }

  @HostListener('window:dragleave', ['$event'])
  protected onLeave(event: Event): void {
    if (!this.watching(event)) {
      return;
    }
    this.depth = Math.max(0, this.depth - 1);
    // Nothing is related to a drag that left the window.
    if (
      this.depth === 0 ||
      (event as { relatedTarget?: EventTarget | null }).relatedTarget == null
    ) {
      this.end();
    }
  }

  @HostListener('window:drop', ['$event'])
  protected onDrop(event: Event): void {
    if (!this.watching(event)) {
      return;
    }
    event.preventDefault();
    this.end();
    const files = Array.from(payload(event)?.files ?? []);
    this.addBooks.open(files, this.section());
  }

  private end(): void {
    this.depth = 0;
    this.dragging.set(false);
  }

  /** Whether `event` is a drag of files and the user may upload them. */
  private watching(event: Event): boolean {
    return this.session.canUpload() && payload(event)?.types?.includes('Files') === true;
  }
}

function payload(event: Event): DragPayload | undefined {
  return (event as { dataTransfer?: DragPayload | null }).dataTransfer ?? undefined;
}
