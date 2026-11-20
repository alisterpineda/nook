import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Session } from '../../core/session';
import { AddBooks } from './add-books';
import { DropOverlay } from './drop-overlay';

describe('DropOverlay', () => {
  const canUpload = signal(true);
  const open = vi.fn<(files: readonly File[], sectionParam: string | null) => void>();
  let fixture: ComponentFixture<DropOverlay>;

  beforeEach(() => {
    canUpload.set(true);
    open.mockReset();
    TestBed.configureTestingModule({
      providers: [
        { provide: Session, useValue: { canUpload } },
        { provide: AddBooks, useValue: { open } },
      ],
    });
    fixture = TestBed.createComponent(DropOverlay);
    fixture.componentRef.setInput('section', 'books');
    fixture.detectChanges();
  });

  afterEach(() => fixture.destroy());

  /** Dispatches a drag event on `target`, since jsdom has no `DragEvent`. Returns the event. */
  function drag(
    type: 'dragenter' | 'dragover' | 'dragleave' | 'drop',
    options: { types?: string[]; files?: File[]; relatedTarget?: EventTarget | null } = {},
    target: EventTarget = window,
  ): Event {
    const event = new Event(type, { bubbles: true, cancelable: true });
    Object.defineProperty(event, 'dataTransfer', {
      value: { types: options.types ?? ['Files'], files: options.files ?? [] },
    });
    Object.defineProperty(event, 'relatedTarget', { value: options.relatedTarget ?? null });
    target.dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  function showing(): boolean {
    return (
      (fixture.nativeElement as HTMLElement).textContent?.includes('Drop to add books') ?? false
    );
  }

  it('shows while files are held over the window', () => {
    expect(showing()).toBe(false);
    drag('dragenter');
    expect(showing()).toBe(true);
  });

  it('hides when the drag leaves', () => {
    drag('dragenter');
    drag('dragleave');
    expect(showing()).toBe(false);
  });

  it('stays up while the drag moves between elements', () => {
    drag('dragenter');
    // Entering a child fires enter before the parent's leave, and the pointer is still inside the window.
    drag('dragenter');
    drag('dragleave', { relatedTarget: document.body });
    expect(showing()).toBe(true);
    drag('dragleave');
    expect(showing()).toBe(false);
  });

  it('hides when the drag ends in a drop', () => {
    drag('dragenter');
    drag('drop');
    expect(showing()).toBe(false);
  });

  it('ignores a drag that carries no files', () => {
    drag('dragenter', { types: ['text/plain'] });
    const over = drag('dragover', { types: ['text/plain'] });
    const dropped = drag('drop', { types: ['text/plain'] });
    expect(showing()).toBe(false);
    expect(over.defaultPrevented).toBe(false);
    expect(dropped.defaultPrevented).toBe(false);
    expect(open).not.toHaveBeenCalled();
  });

  it('never shows for a user who may not upload', () => {
    canUpload.set(false);
    drag('dragenter');
    const over = drag('dragover');
    const dropped = drag('drop', { files: [new File(['x'], 'dune.epub')] });
    expect(showing()).toBe(false);
    expect(over.defaultPrevented).toBe(false);
    expect(dropped.defaultPrevented).toBe(false);
    expect(open).not.toHaveBeenCalled();
  });

  it('opens the dialog with the dropped files and the section being browsed', () => {
    const files = [new File(['x'], 'dune.epub'), new File(['y'], 'emma.epub')];
    drag('dragenter');
    expect(drag('dragover').defaultPrevented).toBe(true);
    expect(drag('drop', { files }).defaultPrevented).toBe(true);
    expect(open).toHaveBeenCalledWith(files, 'books');
  });

  it('stops listening once destroyed', () => {
    fixture.destroy();
    drag('dragenter');
    const dropped = drag('drop', { files: [new File(['x'], 'dune.epub')] });
    expect(dropped.defaultPrevented).toBe(false);
    expect(open).not.toHaveBeenCalled();
  });
});
