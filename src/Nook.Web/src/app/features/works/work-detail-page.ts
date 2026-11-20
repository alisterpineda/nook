import { DatePipe } from '@angular/common';
import { Component, ElementRef, inject, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { distinctUntilChanged, map } from 'rxjs';
import { failureOf, reloadable } from '../../core/reloadable';
import { components } from '../../api/schema';
import { WorksApi, partFileUrl } from '../../api/works-api';
import { allows } from '../../core/content-access';
import { formatBytes } from '../../core/format-bytes';
import { PageName } from '../../core/page-name';
import { LoadFailed } from '../../ui/load-failed';
import { LoadingRegion } from '../../ui/loading';
import { pageHeading } from '../../ui/page-heading';
import { NotFound } from '../../ui/not-found';
import { WorkCover } from './work-cover';
import { formatLabel, roleLabel, seriesText } from './work-labels';

type WorkDetailResponse = components['schemas']['WorkDetailResponse'];
type WorkItemResponse = components['schemas']['WorkItemResponse'];

/** Where the page stands for the work the URL names. */
type Showing =
  | { status: 'loading' }
  | { status: 'loaded'; work: WorkDetailResponse }
  | { status: 'not-found' }
  | { status: 'failed' };

/** What the page says, and the header with it, when there is no such work. */
const notFoundHeading = 'Work not found';

/** One work with everything the API knows about it, leaving out what the work does not have. */
@Component({
  selector: 'app-work-detail-page',
  imports: [
    LoadingRegion,
    DatePipe,
    RouterLink,
    HlmBadgeImports,
    HlmButtonImports,
    HlmSkeletonImports,
    LoadFailed,
    NotFound,
    WorkCover,
  ],
  template: `
    @let current = showing();
    @if (current.status === 'loading') {
      <app-loading class="flex max-w-3xl flex-col gap-6">
        <div class="flex items-start gap-4 sm:gap-6" aria-hidden="true">
          <div hlmSkeleton class="aspect-[2/3] w-28 shrink-0 sm:w-64"></div>
          <div class="flex min-w-0 flex-1 flex-col gap-3">
            <div hlmSkeleton class="h-8 w-3/4"></div>
            <div hlmSkeleton class="h-4 w-1/3"></div>
            <div hlmSkeleton class="mt-2 h-9 w-24"></div>
          </div>
        </div>
        <div class="flex flex-col gap-2" aria-hidden="true">
          <div hlmSkeleton class="h-4 w-full"></div>
          <div hlmSkeleton class="h-4 w-full"></div>
          <div hlmSkeleton class="h-4 w-2/3"></div>
        </div>
      </app-loading>
    } @else if (current.status === 'failed') {
      <app-load-failed (retry)="reload()" />
    } @else if (current.status === 'not-found') {
      <app-not-found
        [heading]="notFoundHeading"
        backLink="/library"
        backLabel="Back to the library"
      />
    } @else {
      @let work = current.work;
      <article class="flex max-w-3xl flex-col gap-6">
        <div class="flex items-start gap-4 sm:gap-6">
          <app-work-cover class="w-28 shrink-0 sm:w-64" [work]="work" size="large" />
          <header class="flex min-w-0 flex-col gap-2">
            <h1 [class]="pageHeading">{{ work.title }}</h1>
            @if (work.contributors.length > 0) {
              <ul class="flex flex-wrap gap-x-4 gap-y-1 text-sm" aria-label="Contributors">
                @for (contributor of work.contributors; track $index) {
                  <li>
                    <a
                      class="font-medium hover:underline"
                      [routerLink]="['/people', contributor.personId]"
                      >{{ contributor.name }}</a
                    >
                    <span class="text-muted-foreground ms-1">{{
                      roleLabel(contributor.role)
                    }}</span>
                  </li>
                }
              </ul>
            }
            @let toRead = firstReadable(work);
            @let canDownload = allows(work.access, 'download') && hasFiles(work);
            @if (toRead || canDownload) {
              <div class="mt-2 flex flex-wrap gap-2" role="group" aria-label="Actions">
                @if (toRead) {
                  <a hlmBtn [routerLink]="['/works', work.id, 'read', toRead.id]">Read</a>
                }
                @if (canDownload) {
                  @if (oneFilePerFormat(work)) {
                    @for (item of work.items; track item.id) {
                      <a
                        hlmBtn
                        variant="outline"
                        download
                        [attr.href]="partFileUrl(item.parts[0].id)"
                        >Download {{ formatLabel(item.format) }}</a
                      >
                    }
                  } @else {
                    <!-- Which file is meant is not clear, so this goes to the list of them. -->
                    <button hlmBtn variant="outline" type="button" (click)="showFiles()">
                      Download
                    </button>
                  }
                }
              </div>
            }
          </header>
        </div>

        @if (work.description) {
          <section aria-label="Description">
            <p class="text-sm whitespace-pre-line">{{ work.description }}</p>
          </section>
        }

        @if (hasDetails(work)) {
          <dl class="grid grid-cols-[max-content_1fr] gap-x-6 gap-y-2 text-sm">
            @if (work.series) {
              <dt class="text-muted-foreground">Series</dt>
              <dd>{{ seriesText(work.series) }}</dd>
            }
            @if (work.publisher) {
              <dt class="text-muted-foreground">Publisher</dt>
              <dd>{{ work.publisher }}</dd>
            }
            @if (work.publicationDate) {
              <dt class="text-muted-foreground">Published</dt>
              <dd>{{ work.publicationDate | date: 'longDate' }}</dd>
            }
            @if (work.language) {
              <dt class="text-muted-foreground">Language</dt>
              <dd>{{ work.language }}</dd>
            }
            @if (work.tags.length > 0) {
              <dt class="text-muted-foreground">Tags</dt>
              <dd class="flex flex-wrap gap-1">
                @for (tag of work.tags; track tag.id) {
                  <span hlmBadge variant="outline">{{ tag.name }}</span>
                }
              </dd>
            }
            @if (work.externalIds.length > 0) {
              <dt class="text-muted-foreground">Identifiers</dt>
              <dd>
                <ul class="flex flex-col gap-1">
                  @for (externalId of work.externalIds; track $index) {
                    <li>
                      <span class="text-muted-foreground me-2">{{ externalId.scheme }}</span>
                      <span class="font-mono">{{ externalId.value }}</span>
                    </li>
                  }
                </ul>
              </dd>
            }
            @if (filesAreDetails(work)) {
              <dt class="text-muted-foreground">Files</dt>
              <dd>
                <ul class="flex flex-col gap-1">
                  @for (item of work.items; track item.id) {
                    <li class="flex flex-wrap gap-x-2">
                      <span>{{ formatLabel(item.format) }}</span>
                      <span class="text-muted-foreground">{{ item.parts[0].mediaType }}</span>
                      <span class="text-muted-foreground">{{
                        formatBytes(item.parts[0].size)
                      }}</span>
                    </li>
                  }
                </ul>
              </dd>
            }
          </dl>
        }

        @if (work.items.length > 0 && !filesAreDetails(work)) {
          <section #files class="flex scroll-mt-4 flex-col gap-2" aria-labelledby="files-heading">
            <h2 id="files-heading" class="text-base font-semibold">Files</h2>
            <ul class="flex flex-col gap-3">
              @for (item of work.items; track item.id) {
                <li class="flex flex-col gap-1">
                  <div class="flex flex-wrap items-center gap-2">
                    <span hlmBadge variant="secondary">{{ formatLabel(item.format) }}</span>
                    <!-- With one item to read, the Read beside the cover already opens it. -->
                    @if (readable(work, item) && severalReadable(work)) {
                      <a hlmBtn size="sm" [routerLink]="['/works', work.id, 'read', item.id]"
                        >Read</a
                      >
                    }
                  </div>
                  <ul class="flex flex-col gap-1 text-sm">
                    @for (part of item.parts; track part.id) {
                      <li class="flex flex-wrap items-center gap-2">
                        <span>{{ part.mediaType }}</span>
                        <span class="text-muted-foreground">{{ formatBytes(part.size) }}</span>
                        @if (allows(work.access, 'download')) {
                          <!-- The API names the saved file, so the download attribute carries no name. -->
                          <a
                            hlmBtn
                            variant="outline"
                            size="sm"
                            download
                            [attr.href]="partFileUrl(part.id)"
                            >Download</a
                          >
                        }
                      </li>
                    }
                  </ul>
                </li>
              }
            </ul>
          </section>
        }
      </article>
    }
  `,
})
export class WorkDetailPage {
  protected readonly notFoundHeading = notFoundHeading;
  protected readonly pageHeading = pageHeading;
  private readonly worksApi = inject(WorksApi);
  private readonly route = inject(ActivatedRoute);

  private readonly pageName = inject(PageName);

  protected readonly allows = allows;
  protected readonly formatBytes = formatBytes;
  protected readonly formatLabel = formatLabel;
  protected readonly partFileUrl = partFileUrl;
  protected readonly roleLabel = roleLabel;
  protected readonly seriesText = seriesText;

  private readonly files = viewChild<ElementRef<HTMLElement>>('files');

  // Going from one work to another keeps this page, so it follows the id in the URL rather than reading it once, and
  // an answer for a work the user has already left is dropped.
  private readonly loading = reloadable(
    this.route.paramMap.pipe(
      map((params) => params.get('id') ?? ''),
      distinctUntilChanged(),
    ),
    (id) => this.load(id),
  );

  protected readonly showing = toSignal(this.loading.state$, { requireSync: true });

  /** Asks again for what failed to load. */
  protected readonly reload = this.loading.reload;

  protected hasDetails(work: WorkDetailResponse): boolean {
    return (
      work.series !== null ||
      !!work.publisher ||
      !!work.publicationDate ||
      !!work.language ||
      work.tags.length > 0 ||
      work.externalIds.length > 0 ||
      this.filesAreDetails(work)
    );
  }

  // Reading needs the read level or above. Ingest makes one part per EPUB item, but nothing enforces it, and the reader
  // opens only an item with exactly one.
  protected readable(work: WorkDetailResponse, item: WorkItemResponse): boolean {
    return allows(work.access, 'read') && item.format === 'epub' && item.parts.length === 1;
  }

  /** The item the Read button beside the cover opens: the first one the reader can open. */
  protected firstReadable(work: WorkDetailResponse): WorkItemResponse | undefined {
    return work.items.find((item) => this.readable(work, item));
  }

  protected severalReadable(work: WorkDetailResponse): boolean {
    return work.items.filter((item) => this.readable(work, item)).length > 1;
  }

  protected hasFiles(work: WorkDetailResponse): boolean {
    return work.items.some((item) => item.parts.length > 0);
  }

  /**
   * Whether naming a format names one file: every item has exactly one part and no two items share a format. Only then
   * can the buttons beside the cover download directly.
   */
  protected oneFilePerFormat(work: WorkDetailResponse): boolean {
    const formats = new Set(work.items.map((item) => item.format));
    return (
      formats.size === work.items.length && work.items.every((item) => item.parts.length === 1)
    );
  }

  /**
   * Whether the files are a row of the details rather than a section of their own. They are when each format is one
   * file: whatever the access level allows is then already beside the cover, which leaves the section nothing to offer.
   */
  protected filesAreDetails(work: WorkDetailResponse): boolean {
    return work.items.length > 0 && this.oneFilePerFormat(work);
  }

  protected showFiles(): void {
    this.files()?.nativeElement.scrollIntoView({ behavior: 'smooth' });
  }

  private async load(id: string): Promise<Showing> {
    try {
      const work = await this.worksApi.get(id);
      this.pageName.set(id, work.title);
      return { status: 'loaded', work };
    } catch (error) {
      const status = failureOf(error);
      // The header waits for a name, so it is told what the page shows in place of one.
      this.pageName.set(id, status === 'not-found' ? notFoundHeading : 'Work');
      return { status };
    }
  }
}
