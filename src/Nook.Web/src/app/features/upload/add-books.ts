import { Injectable, WritableSignal, inject, signal } from '@angular/core';
import { type BrnDialogRef } from '@spartan-ng/brain/dialog';
import { HlmDialogService } from '@spartan-ng/helm/dialog';
import { AddBooksDialog, AddBooksContext } from './add-books-dialog';

/** Opens the dialog that adds books. There is one at a time. */
@Injectable({ providedIn: 'root' })
export class AddBooks {
  private readonly dialogs = inject(HlmDialogService);
  private current: BrnDialogRef | null = null;
  /** The files the open dialog lists. */
  private listed: WritableSignal<readonly File[]> = signal([]);

  /**
   * Opens the dialog listing `files`, which may be none, with the section named by `sectionParam` preselected. It is
   * the value of the URL's `section` query parameter for the area being browsed, as `areaParam` gives it. While
   * the dialog is already open, `files` join its list and its section stays as chosen.
   */
  open(files: readonly File[], sectionParam: string | null): void {
    if (this.current) {
      this.listed.update((listed) => [...listed, ...files]);
      return;
    }
    this.listed = signal(files);
    const context: AddBooksContext = { files: this.listed, sectionParam };
    const ref = this.dialogs.open(AddBooksDialog, {
      context,
      showCloseButton: false,
      contentClass: 'sm:max-w-lg',
    });
    this.current = ref;
    ref.closed$.subscribe(() => {
      if (this.current === ref) {
        this.current = null;
      }
    });
  }
}
