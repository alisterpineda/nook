import { Component, WritableSignal, computed, inject, linkedSignal, signal } from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { SectionsApi } from '../../api/sections-api';
import { UploadsApi } from '../../api/uploads-api';
import { components } from '../../api/schema';
import { defaultArea } from '../../core/library-area';
import { UploadQueue } from './upload-queue';

type SectionResponse = components['schemas']['SectionResponse'];

/** What the dialog is opened with. */
export interface AddBooksContext {
  /** The files to list, which may be none. Whoever opened the dialog may add to them while it is open. */
  readonly files: WritableSignal<readonly File[]>;
  /** The `section` query parameter of the area being browsed: a section's id, `default`, or `null` for every work. */
  readonly sectionParam: string | null;
}

/** The dialog that lists the files to add, each removable, and sends them to one section when Add is pressed. */
@Component({
  selector: 'app-add-books-dialog',
  imports: [HlmButtonImports, HlmDialogImports, HlmInputImports, HlmLabelImports],
  template: `
    <hlm-dialog-header>
      <h2 hlmDialogTitle>Add books</h2>
      <p hlmDialogDescription>Choose EPUB files, then the section they go to.</p>
    </hlm-dialog-header>
    <div class="grid gap-2">
      <label hlmLabel for="add-books-section">Section</label>
      <select hlmInput id="add-books-section" (change)="chooseSection($event)">
        <option [value]="defaultArea" [selected]="selectedSectionId() === null">Default</option>
        @for (section of sections(); track section.id) {
          <option [value]="section.id" [selected]="section.id === selectedSectionId()">
            {{ section.name }}
          </option>
        }
      </select>
    </div>
    <div class="grid gap-2">
      <label hlmLabel for="add-books-files">Choose files</label>
      <input
        hlmInput
        id="add-books-files"
        type="file"
        multiple
        accept=".epub,application/epub+zip"
        (change)="picked($event)"
      />
    </div>
    @if (entries().length > 0) {
      <ul class="max-h-64 divide-y overflow-y-auto rounded-lg border" aria-label="Files">
        @for (entry of entries(); track entry.file) {
          <li class="flex flex-wrap items-center gap-x-3 gap-y-1 p-3">
            <span data-file-name class="min-w-0 flex-1 truncate text-sm">{{
              entry.file.name
            }}</span>
            @if (entry.tooLarge) {
              <span class="text-destructive text-sm">Too large, so it is left out</span>
            }
            <button
              hlmBtn
              variant="ghost"
              size="sm"
              type="button"
              [attr.aria-label]="'Remove ' + entry.file.name"
              (click)="remove(entry.file)"
            >
              Remove
            </button>
          </li>
        }
      </ul>
    }
    <hlm-dialog-footer>
      <button hlmBtn variant="outline" type="button" (click)="cancel()">Cancel</button>
      <button hlmBtn type="button" [disabled]="toSend().length === 0" (click)="add()">Add</button>
    </hlm-dialog-footer>
  `,
})
export class AddBooksDialog {
  private readonly context = injectBrnDialogContext<AddBooksContext>();
  private readonly dialog = inject<BrnDialogRef>(BrnDialogRef);
  private readonly queue = inject(UploadQueue);

  protected readonly defaultArea = defaultArea;
  protected readonly files = this.context.files;
  /** The sections the user can see. Without them the picker offers Default alone. */
  protected readonly sections = signal<readonly SectionResponse[]>([]);
  /** The largest file the server takes, or `null` until known or when it could not be fetched. */
  private readonly maxBytes = signal<number | null>(null);

  /** The section files go to, or `null` for the Default area: the one browsed when the user can see it. */
  protected readonly selectedSectionId = linkedSignal<string | null>(
    () => this.sections().find((section) => section.id === this.context.sectionParam)?.id ?? null,
  );

  protected readonly entries = computed(() => {
    const max = this.maxBytes();
    return this.files().map((file) => ({ file, tooLarge: max !== null && file.size > max }));
  });
  /** The files to send: those within the limit. */
  protected readonly toSend = computed(() =>
    this.entries()
      .filter((entry) => !entry.tooLarge)
      .map((entry) => entry.file),
  );

  constructor() {
    // Either can fail without hurting the dialog: Default is always there, and the server refuses a file too large.
    void inject(SectionsApi)
      .list()
      .then((sections) => this.sections.set(sections))
      .catch(() => undefined);
    void inject(UploadsApi)
      .limits()
      .then((limits) => this.maxBytes.set(limits.maxBytes))
      .catch(() => undefined);
  }

  protected chooseSection(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.selectedSectionId.set(value === defaultArea ? null : value);
  }

  protected picked(event: Event): void {
    const input = event.target as HTMLInputElement;
    const picked = Array.from(input.files ?? []);
    this.files.update((files) => [...files, ...picked]);
    // Lets the same file be picked again, which would otherwise raise no change.
    input.value = '';
  }

  protected remove(file: File): void {
    this.files.update((files) => files.filter((candidate) => candidate !== file));
  }

  protected add(): void {
    this.queue.add(this.toSend(), this.selectedSectionId());
    this.dialog.close();
  }

  protected cancel(): void {
    this.dialog.close();
  }
}
