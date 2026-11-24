import { Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormArray,
  FormControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import { distinctUntilChanged, map } from 'rxjs';
import { components } from '../../api/schema';
import { SectionsApi } from '../../api/sections-api';
import { WorksApi } from '../../api/works-api';
import { somethingWentWrong } from '../../core/messages';
import { PageName } from '../../core/page-name';
import { failureOf, reloadable } from '../../core/reloadable';
import { LoadFailed } from '../../ui/load-failed';
import { LoadingRegion } from '../../ui/loading';
import { NotFound } from '../../ui/not-found';
import { pageHeading } from '../../ui/page-heading';
import { NameListField } from './name-list-field';
import { unshownWorkName, workNotFoundHeading } from './work-labels';

type SectionResponse = components['schemas']['SectionResponse'];
type WorkDetailResponse = components['schemas']['WorkDetailResponse'];

/** What the work's form holds. The section is a section's id, or empty for the Default area. */
function buildForm(formBuilder: NonNullableFormBuilder, work: WorkDetailResponse) {
  const names = (values: string[]) =>
    formBuilder.array(values.map((value) => formBuilder.control(value)));
  return formBuilder.group({
    title: formBuilder.control(work.title, notBlank),
    sortTitle: formBuilder.control(work.sortTitle, notBlank),
    authors: names(
      work.contributors.filter(({ role }) => role === 'author').map(({ name }) => name),
    ),
    seriesName: work.series?.name ?? '',
    seriesPosition: formBuilder.control<number | null>(work.series?.position ?? null),
    tags: names(work.tags.map(({ name }) => name)),
    description: work.description ?? '',
    sectionId: work.sectionId ?? '',
  });
}

type WorkForm = ReturnType<typeof buildForm>;

function notBlank(control: AbstractControl<string>): ValidationErrors | null {
  return control.value.trim() === '' ? { required: true } : null;
}

/** The text of each field with something in it, trimmed. */
function filled(names: FormArray<FormControl<string>>): string[] {
  return names
    .getRawValue()
    .map((name) => name.trim())
    .filter((name) => name !== '');
}

/** Where the page stands for the work the URL names. */
type Showing =
  | { status: 'loading' }
  | { status: 'loaded'; work: WorkDetailResponse; sections: SectionResponse[]; form: WorkForm }
  | { status: 'not-found' }
  | { status: 'failed' };

/** A form to change a work's title, sort title, authors, series, tags, description and section, and save them. */
@Component({
  selector: 'app-work-edit-page',
  imports: [
    LoadingRegion,
    ReactiveFormsModule,
    RouterLink,
    HlmButtonImports,
    HlmInputImports,
    HlmLabelImports,
    HlmSkeletonImports,
    HlmTextareaImports,
    LoadFailed,
    NameListField,
    NotFound,
  ],
  template: `
    @let current = showing();
    @if (current.status === 'loading') {
      <app-loading class="grid max-w-xl gap-6">
        <div hlmSkeleton class="h-8 w-1/2" aria-hidden="true"></div>
        <div hlmSkeleton class="h-9 w-full" aria-hidden="true"></div>
        <div hlmSkeleton class="h-9 w-full" aria-hidden="true"></div>
        <div hlmSkeleton class="h-24 w-full" aria-hidden="true"></div>
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
      @let form = current.form;
      <h1 class="mb-6" [class]="pageHeading">Edit details</h1>
      <form class="grid max-w-xl gap-6" [formGroup]="form" (ngSubmit)="save(current.work, form)">
        <div class="grid gap-2">
          <label hlmLabel for="work-title">Title</label>
          <input
            hlmInput
            id="work-title"
            formControlName="title"
            [forceInvalid]="showsInvalid(form, 'title')"
          />
          @if (showsInvalid(form, 'title')) {
            <p class="text-destructive text-sm" role="alert">Enter a title.</p>
          }
        </div>
        <div class="grid gap-2">
          <label hlmLabel for="work-sort-title">Sort title</label>
          <input
            hlmInput
            id="work-sort-title"
            formControlName="sortTitle"
            [forceInvalid]="showsInvalid(form, 'sortTitle')"
          />
          @if (showsInvalid(form, 'sortTitle')) {
            <p class="text-destructive text-sm" role="alert">Enter a sort title.</p>
          }
        </div>
        <app-name-list-field
          legend="Authors"
          label="Author"
          [names]="form.controls.authors"
          [ordered]="true"
        />
        <div class="grid gap-2">
          <label hlmLabel for="work-series">Series</label>
          <input hlmInput id="work-series" formControlName="seriesName" />
        </div>
        <div class="grid gap-2">
          <label hlmLabel for="work-series-position">Position in the series</label>
          <input
            hlmInput
            id="work-series-position"
            type="number"
            step="any"
            formControlName="seriesPosition"
          />
        </div>
        <app-name-list-field legend="Tags" label="Tag" [names]="form.controls.tags" />
        <div class="grid gap-2">
          <label hlmLabel for="work-description">Description</label>
          <textarea hlmTextarea id="work-description" formControlName="description"></textarea>
        </div>
        <div class="grid gap-2">
          <label hlmLabel for="work-section">Section</label>
          <select hlmInput id="work-section" formControlName="sectionId">
            <option value="">Default</option>
            @for (section of current.sections; track section.id) {
              <option [value]="section.id">{{ section.name }}</option>
            }
          </select>
        </div>
        @if (failed()) {
          <p class="text-destructive text-sm" role="alert">{{ somethingWentWrong }}</p>
        }
        <div class="flex gap-2">
          <button hlmBtn type="submit" [disabled]="saving()">Save</button>
          <a hlmBtn variant="outline" [routerLink]="['/works', current.work.id]">Cancel</a>
        </div>
      </form>
    }
  `,
})
export class WorkEditPage {
  protected readonly notFoundHeading = workNotFoundHeading;
  protected readonly pageHeading = pageHeading;
  protected readonly somethingWentWrong = somethingWentWrong;
  private readonly worksApi = inject(WorksApi);
  private readonly sectionsApi = inject(SectionsApi);
  private readonly formBuilder = inject(NonNullableFormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly pageName = inject(PageName);

  protected readonly saving = signal(false);
  protected readonly failed = signal(false);
  private readonly attempted = signal(false);

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

  /** Whether to mark a field wrong: only once the user has tried to save. */
  protected showsInvalid(form: WorkForm, field: 'title' | 'sortTitle'): boolean {
    return this.attempted() && form.controls[field].invalid;
  }

  protected async save(work: WorkDetailResponse, form: WorkForm): Promise<void> {
    if (this.saving()) {
      return;
    }
    this.attempted.set(true);
    this.failed.set(false);
    if (form.invalid) {
      return;
    }
    const value = form.getRawValue();
    const seriesName = value.seriesName.trim();
    const description = value.description.trim();
    const title = value.title.trim();
    this.saving.set(true);
    try {
      await this.worksApi.update(work.id, {
        title,
        sortTitle: value.sortTitle.trim(),
        authors: filled(form.controls.authors),
        series: seriesName === '' ? null : { name: seriesName, position: value.seriesPosition },
        tags: filled(form.controls.tags),
        description: description === '' ? null : value.description,
        sectionId: value.sectionId === '' ? null : value.sectionId,
      });
    } catch {
      this.failed.set(true);
      return;
    } finally {
      this.saving.set(false);
    }
    this.pageName.set(work.id, title);
    await this.router.navigate(['/works', work.id]);
  }

  private async load(id: string): Promise<Showing> {
    this.attempted.set(false);
    this.failed.set(false);
    try {
      const [work, sections] = await Promise.all([this.worksApi.get(id), this.sectionsApi.list()]);
      this.pageName.set(id, work.title);
      if (!work.canEdit) {
        await this.router.navigate(['/works', id], { replaceUrl: true });
        return { status: 'loading' };
      }
      return { status: 'loaded', work, sections, form: buildForm(this.formBuilder, work) };
    } catch (error) {
      const status = failureOf(error);
      this.pageName.set(id, unshownWorkName(status));
      return { status };
    }
  }
}
