import { Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { BadgeVariants, HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { map } from 'rxjs';
import { LibrariesApi } from '../../api/libraries-api';
import { components } from '../../api/schema';
import { somethingWentWrong } from '../../core/messages';
import { UploadQueue, UploadRow, UploadStatus, isFinished } from './upload-queue';

type LibraryResponse = components['schemas']['LibraryResponse'];

/** Where the libraries files can be added to stand. */
type Libraries =
  { status: 'loading' } | { status: 'loaded'; libraries: LibraryResponse[] } | { status: 'failed' };

/** How each status reads on a row, and the badge it wears. */
const statusLabels: Record<UploadStatus, { text: string; variant: BadgeVariants['variant'] }> = {
  waiting: { text: 'Waiting', variant: 'outline' },
  uploading: { text: 'Uploading', variant: 'secondary' },
  ingesting: { text: 'Ingesting', variant: 'secondary' },
  added: { text: 'Added', variant: 'default' },
  duplicate: { text: 'Duplicate', variant: 'outline' },
  failed: { text: 'Failed', variant: 'destructive' },
};

/**
 * Adds books by sending the files picked or dropped to the upload queue, each to the library selected when it was
 * added. A library picker shows only when there are several, preselected with the library the URL names. Below,
 * a row per file follows it to its result, oldest first.
 */
@Component({
  selector: 'app-upload-page',
  imports: [RouterLink, HlmBadgeImports, HlmButtonImports, HlmInputImports, HlmLabelImports],
  template: `
    <h1 class="mb-4 text-lg font-semibold">Add books</h1>
    @let current = libraries();
    @if (current.status === 'failed') {
      <p class="text-destructive text-sm" role="alert">{{ somethingWentWrong }}</p>
    } @else if (current.status === 'loaded') {
      @if (current.libraries.length === 0) {
        <p class="text-muted-foreground text-sm">There is no library to add books to.</p>
      } @else {
        @let selected = selectedLibraryId();
        @if (current.libraries.length > 1) {
          <div class="mb-4 grid max-w-xs gap-2">
            <label hlmLabel for="upload-library">Library</label>
            <select hlmInput id="upload-library" (change)="chooseLibrary($event)">
              <option value="" disabled [selected]="selected === null">Choose a library</option>
              @for (library of current.libraries; track library.id) {
                <option [value]="library.id" [selected]="library.id === selected">
                  {{ library.name }}
                </option>
              }
            </select>
          </div>
        }
        <div
          data-drop-zone
          class="flex flex-col items-center gap-3 rounded-lg border-2 border-dashed p-8 text-center"
          [class.opacity-50]="selected === null"
          [attr.aria-disabled]="selected === null ? 'true' : null"
          (dragover)="allowDrop($event)"
          (drop)="dropped($event)"
        >
          <p class="text-muted-foreground text-sm">Drop EPUB files here, or choose them.</p>
          <label hlmLabel class="sr-only" for="upload-files">Choose files</label>
          <input
            hlmInput
            id="upload-files"
            class="max-w-xs"
            type="file"
            multiple
            accept=".epub,application/epub+zip"
            [disabled]="selected === null"
            (change)="picked($event)"
          />
        </div>
        @if (selected === null) {
          <p class="text-muted-foreground mt-2 text-sm">Choose a library to add books to.</p>
        }
      }
    }
    @if (rows().length > 0) {
      <div class="mt-6 mb-2 flex items-center justify-between gap-2">
        <h2 class="text-base font-semibold">Files</h2>
        @if (anyFinished()) {
          <button hlmBtn variant="outline" size="sm" type="button" (click)="clearFinished()">
            Clear finished
          </button>
        }
      </div>
      <ul class="divide-y rounded-lg border" aria-label="Files">
        @for (row of rows(); track row.id) {
          @let label = statusLabels[row.status];
          <li class="flex flex-wrap items-center gap-x-3 gap-y-1 p-3">
            @if (row.workId && isFinished(row)) {
              <a
                data-file-name
                class="min-w-0 flex-1 truncate text-sm font-medium underline-offset-4 hover:underline"
                [routerLink]="['/works', row.workId]"
                >{{ row.name }}</a
              >
            } @else {
              <span data-file-name class="min-w-0 flex-1 truncate text-sm">{{ row.name }}</span>
            }
            <span hlmBadge [variant]="label.variant">{{ label.text }}</span>
            @if (row.status === 'failed') {
              <div class="flex gap-2">
                <button hlmBtn variant="outline" size="sm" type="button" (click)="retry(row)">
                  Retry
                </button>
                <button hlmBtn variant="ghost" size="sm" type="button" (click)="remove(row)">
                  Remove
                </button>
              </div>
              <p class="text-destructive w-full text-sm">{{ row.error }}</p>
            }
          </li>
        }
      </ul>
    }
  `,
})
export class UploadPage {
  private readonly librariesApi = inject(LibrariesApi);
  private readonly queue = inject(UploadQueue);

  protected readonly somethingWentWrong = somethingWentWrong;
  protected readonly statusLabels = statusLabels;
  protected readonly rows = this.queue.rows;
  /** Whether any row was added or found to be a duplicate, which "Clear finished" takes away. */
  protected readonly isFinished = isFinished;
  protected readonly anyFinished = computed(() => this.rows().some(isFinished));
  protected readonly libraries = signal<Libraries>({ status: 'loading' });

  /** The library the URL names as the one being browsed, which may be none the user can see. */
  private readonly browsedLibraryId = toSignal(
    inject(ActivatedRoute).queryParamMap.pipe(map((params) => params.get('library'))),
    { requireSync: true },
  );

  /** The library files go to when added: the only one, else the one browsed, until the user chooses another. */
  protected readonly selectedLibraryId = linkedSignal(
    computed(() => {
      const current = this.libraries();
      if (current.status !== 'loaded') {
        return null;
      }
      if (current.libraries.length === 1) {
        return current.libraries[0].id;
      }
      const browsed = this.browsedLibraryId();
      return current.libraries.find((library) => library.id === browsed)?.id ?? null;
    }),
  );

  constructor() {
    void this.loadLibraries();
  }

  protected chooseLibrary(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.selectedLibraryId.set(value === '' ? null : value);
  }

  protected allowDrop(event: Event): void {
    // Without this the browser opens a dropped file in place of the app.
    event.preventDefault();
    const transfer = (event as DragEvent).dataTransfer;
    if (transfer) {
      transfer.dropEffect = this.selectedLibraryId() === null ? 'none' : 'copy';
    }
  }

  protected dropped(event: Event): void {
    event.preventDefault();
    this.add((event as DragEvent).dataTransfer?.files);
  }

  protected picked(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.add(input.files);
    // Lets the same file be picked again, which would otherwise raise no change.
    input.value = '';
  }

  protected retry(row: UploadRow): void {
    void this.queue.retry(row);
  }

  protected remove(row: UploadRow): void {
    void this.queue.remove(row);
  }

  protected clearFinished(): void {
    this.queue.clearFinished();
  }

  /** Sends every file to the selected library whatever its type: the server decides what it takes. */
  private add(files: ArrayLike<File> | null | undefined): void {
    const libraryId = this.selectedLibraryId();
    if (libraryId === null || !files || files.length === 0) {
      return;
    }
    this.queue.add(Array.from(files), libraryId);
  }

  private async loadLibraries(): Promise<void> {
    try {
      this.libraries.set({ status: 'loaded', libraries: await this.librariesApi.list() });
    } catch {
      this.libraries.set({ status: 'failed' });
    }
  }
}
