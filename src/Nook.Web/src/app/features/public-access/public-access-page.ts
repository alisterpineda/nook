import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { PublicAccessApi } from '../../api/public-access-api';
import { components } from '../../api/schema';
import { ContentAccess } from '../../core/content-access';
import { somethingWentWrong } from '../../core/messages';
import { LevelSelect } from './level-select';

type PublicAccessSectionResponse = components['schemas']['PublicAccessSectionResponse'];

/** Where the saved settings stand. */
type Loading = 'loading' | 'loaded' | 'failed';

/** How the last save went, if there was one. */
type Saving = 'idle' | 'saving' | 'saved' | 'failed';

/**
 * Lets an admin open parts of the library to a visitor with no account: a switch for public access, the Anonymous
 * level, which caps every other, and a level for the Default area and for each section. "Save" replaces all of them.
 */
@Component({
  selector: 'app-public-access-page',
  imports: [ReactiveFormsModule, HlmButtonImports, HlmLabelImports, LevelSelect],
  template: `
    <h1 class="mb-4 text-lg font-semibold">Public access</h1>
    @switch (loading()) {
      @case ('failed') {
        <p class="text-destructive text-sm" role="alert">{{ somethingWentWrong }}</p>
      }
      @case ('loaded') {
        <form class="grid max-w-md gap-6" [formGroup]="form" (ngSubmit)="save()">
          <div class="flex items-center gap-2">
            <input id="public-access-enabled" type="checkbox" formControlName="enabled" />
            <label hlmLabel for="public-access-enabled">Let visitors with no account in</label>
          </div>
          <div class="text-muted-foreground grid gap-2 text-sm">
            <p>
              Browse shows a book's details and cover, Read opens it in the reader, and Download
              hands over its files. Each level gives everything the ones before it give.
            </p>
            <p>
              No level can go above the Anonymous level: the Default area or a section set higher
              gets the Anonymous level. A section left at None stays closed.
            </p>
            <p>
              Read lets a visitor's browser fetch the whole book, so it makes copying harder but
              does not prevent it.
            </p>
          </div>
          <app-level-select
            fieldId="public-access-access"
            label="Anonymous"
            [control]="form.controls.access"
          />
          <fieldset class="grid gap-4">
            <legend class="mb-2 text-base font-semibold">Areas</legend>
            <app-level-select
              fieldId="public-access-default"
              label="Default"
              [control]="form.controls.defaultAreaAccess"
            />
            @for (section of sections(); track section.id; let index = $index) {
              <app-level-select
                [fieldId]="'public-access-section-' + section.id"
                [label]="section.name"
                [control]="form.controls.sections.controls[index]"
              />
            }
          </fieldset>
          @switch (saving()) {
            @case ('saved') {
              <p class="text-sm" role="status">Saved.</p>
            }
            @case ('failed') {
              <p class="text-destructive text-sm" role="alert">{{ somethingWentWrong }}</p>
            }
          }
          <button
            hlmBtn
            class="justify-self-start"
            type="submit"
            [disabled]="saving() === 'saving'"
          >
            Save
          </button>
        </form>
      }
    }
  `,
})
export class PublicAccessPage {
  private readonly api = inject(PublicAccessApi);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  protected readonly somethingWentWrong = somethingWentWrong;
  protected readonly loading = signal<Loading>('loading');
  protected readonly saving = signal<Saving>('idle');
  /** Each section, in the order of the form's `sections` levels. */
  protected readonly sections = signal<PublicAccessSectionResponse[]>([]);
  protected readonly form = this.formBuilder.group({
    enabled: false,
    access: this.formBuilder.control<ContentAccess>('none'),
    defaultAreaAccess: this.formBuilder.control<ContentAccess>('none'),
    sections: this.formBuilder.array<ContentAccess>([]),
  });

  /** Whether the form changed after the save now on its way was sent, so that save does not cover what it shows. */
  private editedSinceSent = false;

  constructor() {
    // What the last save reported says nothing about a form that has changed since.
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.editedSinceSent = true;
      if (this.saving() !== 'saving') {
        this.saving.set('idle');
      }
    });
    void this.load();
  }

  /** Sends every setting, each section with the level chosen for it, even one above the Anonymous level. */
  protected async save(): Promise<void> {
    if (this.saving() === 'saving') {
      return;
    }
    this.saving.set('saving');
    this.editedSinceSent = false;
    const { enabled, access, defaultAreaAccess, sections } = this.form.getRawValue();
    try {
      await this.api.save({
        enabled,
        access,
        defaultAreaAccess,
        sections: this.sections().map(({ id }, index) => ({ id, access: sections[index] })),
      });
    } catch {
      this.saving.set('failed');
      return;
    }
    this.saving.set(this.editedSinceSent ? 'idle' : 'saved');
  }

  private async load(): Promise<void> {
    try {
      const settings = await this.api.get();
      this.sections.set(settings.sections);
      this.form.setControl(
        'sections',
        this.formBuilder.array(
          settings.sections.map(({ access }) => this.formBuilder.control(access)),
        ),
      );
      this.form.patchValue({
        enabled: settings.enabled,
        access: settings.access,
        defaultAreaAccess: settings.defaultAreaAccess,
      });
      this.loading.set('loaded');
    } catch {
      this.loading.set('failed');
    }
  }
}
