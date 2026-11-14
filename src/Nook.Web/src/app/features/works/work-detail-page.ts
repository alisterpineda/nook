import { DatePipe } from '@angular/common';
import { HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { distinctUntilChanged, from, map, startWith, switchMap } from 'rxjs';
import { components } from '../../api/schema';
import { WorksApi, partFileUrl } from '../../api/works-api';
import { formatBytes } from '../../core/format-bytes';
import { somethingWentWrong } from '../../core/messages';
import { WorkCover } from './work-cover';
import { formatLabel, roleLabel, seriesText } from './work-labels';

type WorkDetailResponse = components['schemas']['WorkDetailResponse'];

/** Where the page stands for the work the URL names. */
type Showing =
  | { status: 'loading' }
  | { status: 'loaded'; work: WorkDetailResponse }
  | { status: 'not-found' }
  | { status: 'failed' };

/** One work with everything the API knows about it, leaving out what the work does not have. */
@Component({
  selector: 'app-work-detail-page',
  imports: [DatePipe, HlmBadgeImports, HlmButtonImports, WorkCover],
  template: `
    @let current = showing();
    @if (current.status === 'failed') {
      <p class="text-destructive text-sm" role="alert">{{ somethingWentWrong }}</p>
    } @else if (current.status === 'not-found') {
      <h1 class="text-lg font-semibold">Work not found</h1>
    } @else if (current.status === 'loaded') {
      @let work = current.work;
      <article class="flex max-w-3xl flex-col gap-6">
        <div class="flex flex-col gap-6 sm:flex-row sm:items-start">
          <app-work-cover class="w-64 max-w-full shrink-0" [work]="work" size="large" />
          <header class="flex min-w-0 flex-col gap-2">
            <h1 class="text-2xl font-semibold">{{ work.title }}</h1>
            @if (work.contributors.length > 0) {
              <ul class="flex flex-wrap gap-x-4 gap-y-1 text-sm" aria-label="Contributors">
                @for (contributor of work.contributors; track $index) {
                  <li>
                    <span class="font-medium">{{ contributor.name }}</span>
                    <span class="text-muted-foreground ms-1">{{
                      roleLabel(contributor.role)
                    }}</span>
                  </li>
                }
              </ul>
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
          </dl>
        }

        @if (work.items.length > 0) {
          <section class="flex flex-col gap-2">
            <h2 class="text-base font-semibold">Files</h2>
            <ul class="flex flex-col gap-3">
              @for (item of work.items; track item.id) {
                <li class="flex flex-col gap-1">
                  <span hlmBadge variant="secondary">{{ formatLabel(item.format) }}</span>
                  <ul class="flex flex-col gap-1 text-sm">
                    @for (part of item.parts; track part.id) {
                      <li class="flex flex-wrap items-center gap-2">
                        <span>{{ part.mediaType }}</span>
                        <span class="text-muted-foreground">{{ formatBytes(part.size) }}</span>
                        <!-- The API names the saved file, so the download attribute carries no name. -->
                        <a
                          hlmBtn
                          variant="outline"
                          size="sm"
                          download
                          [attr.href]="partFileUrl(part.id)"
                          >Download</a
                        >
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
  private readonly worksApi = inject(WorksApi);
  private readonly route = inject(ActivatedRoute);

  protected readonly somethingWentWrong = somethingWentWrong;
  protected readonly formatBytes = formatBytes;
  protected readonly formatLabel = formatLabel;
  protected readonly partFileUrl = partFileUrl;
  protected readonly roleLabel = roleLabel;
  protected readonly seriesText = seriesText;

  // Going from one work to another keeps this page, so it follows the id in the URL rather than reading it once, and
  // an answer for a work the user has already left is dropped.
  protected readonly showing = toSignal(
    this.route.paramMap.pipe(
      map((params) => params.get('id') ?? ''),
      distinctUntilChanged(),
      switchMap((id) => from(this.load(id)).pipe(startWith<Showing>({ status: 'loading' }))),
    ),
    { requireSync: true },
  );

  protected hasDetails(work: WorkDetailResponse): boolean {
    return (
      work.series !== null ||
      !!work.publisher ||
      !!work.publicationDate ||
      !!work.language ||
      work.tags.length > 0 ||
      work.externalIds.length > 0
    );
  }

  private async load(id: string): Promise<Showing> {
    try {
      return { status: 'loaded', work: await this.worksApi.get(id) };
    } catch (error) {
      return error instanceof HttpErrorResponse && error.status === HttpStatusCode.NotFound
        ? { status: 'not-found' }
        : { status: 'failed' };
    }
  }
}
