import { Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { BadgeVariants, HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { map } from 'rxjs';
import { SectionsApi } from '../../api/sections-api';
import { components } from '../../api/schema';
import { defaultArea, readArea } from '../../core/library-area';
import { somethingWentWrong } from '../../core/messages';
import { UploadQueue, UploadRow, UploadStatus, isFinished } from './upload-queue';

type SectionResponse = components['schemas']['SectionResponse'];

/** Where the sections files can be added to stand. */
type Sections =
  { status: 'loading' } | { status: 'loaded'; sections: SectionResponse[] } | { status: 'failed' };

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
 * Adds books by sending the files picked or dropped to the upload queue, each to the section selected when it was
 * added, or to the Default area. A section picker shows only when there are sections, offering Default first and
 * preselected with the section the URL names, else Default. Below, a row per file follows it to its result, oldest
 * first.
 */
@Component({
  selector: 'app-upload-page',
  imports: [RouterLink, HlmBadgeImports, HlmButtonImports, HlmInputImports, HlmLabelImports],
  template: `
    <h1 class="mb-4 text-lg font-semibold">Add books</h1>
    @let current = sections();
    @if (current.status === 'failed') {
      <p class="text-destructive text-sm" role="alert">{{ somethingWentWrong }}</p>
    } @else if (current.status === 'loaded') {
      @if (current.sections.length > 0) {
        @let selected = selectedSectionId();
        <div class="mb-4 grid max-w-xs gap-2">
          <label hlmLabel for="upload-section">Section</label>
          <select hlmInput id="upload-section" (change)="chooseSection($event)">
            <option [value]="defaultArea" [selected]="selected === null">Default</option>
            @for (section of current.sections; track section.id) {
              <option [value]="section.id" [selected]="section.id === selected">
                {{ section.name }}
              </option>
            }
          </select>
        </div>
      }
      <div
        data-drop-zone
        class="flex flex-col items-center gap-3 rounded-lg border-2 border-dashed p-8 text-center"
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
          (change)="picked($event)"
        />
      </div>
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
  private readonly sectionsApi = inject(SectionsApi);
  private readonly queue = inject(UploadQueue);

  protected readonly somethingWentWrong = somethingWentWrong;
  protected readonly defaultArea = defaultArea;
  protected readonly statusLabels = statusLabels;
  protected readonly rows = this.queue.rows;
  /** Whether any row was added or found to be a duplicate, which "Clear finished" takes away. */
  protected readonly isFinished = isFinished;
  protected readonly anyFinished = computed(() => this.rows().some(isFinished));
  protected readonly sections = signal<Sections>({ status: 'loading' });

  /** The area the URL names as the one being browsed, which may be a section the user cannot see. */
  private readonly browsedArea = toSignal(
    inject(ActivatedRoute).queryParamMap.pipe(map(readArea)),
    {
      requireSync: true,
    },
  );

  /**
   * The section files go to when added, or `null` for the Default area: the one browsed when the user can see it,
   * until the user chooses another.
   */
  protected readonly selectedSectionId = linkedSignal(
    computed(() => {
      const current = this.sections();
      if (current.status !== 'loaded') {
        return null;
      }
      const browsed = this.browsedArea();
      if (browsed.kind !== 'section') {
        return null;
      }
      return current.sections.find((section) => section.id === browsed.id)?.id ?? null;
    }),
  );

  constructor() {
    void this.loadSections();
  }

  protected chooseSection(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.selectedSectionId.set(value === defaultArea ? null : value);
  }

  protected allowDrop(event: Event): void {
    // Without this the browser opens a dropped file in place of the app.
    event.preventDefault();
    const transfer = (event as DragEvent).dataTransfer;
    if (transfer) {
      transfer.dropEffect = 'copy';
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

  /** Sends every file to the selected section whatever its type: the server decides what it takes. */
  private add(files: ArrayLike<File> | null | undefined): void {
    if (!files || files.length === 0) {
      return;
    }
    this.queue.add(Array.from(files), this.selectedSectionId());
  }

  private async loadSections(): Promise<void> {
    try {
      this.sections.set({ status: 'loaded', sections: await this.sectionsApi.list() });
    } catch {
      this.sections.set({ status: 'failed' });
    }
  }
}
