import { Component, input } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { ContentAccess, contentAccessLevels } from '../../core/content-access';

/** How each level reads in a list. */
const labels: Record<ContentAccess, string> = {
  none: 'None',
  browse: 'Browse',
  read: 'Read',
  download: 'Download',
};

/** A labelled list of the levels, from least to most, that sets `control`. */
@Component({
  selector: 'app-level-select',
  imports: [ReactiveFormsModule, HlmInputImports, HlmLabelImports],
  host: { class: 'grid gap-2' },
  template: `
    <label hlmLabel [for]="fieldId()">{{ label() }}</label>
    <select hlmInput [id]="fieldId()" [formControl]="control()">
      @for (level of levels; track level.value) {
        <option [value]="level.value">{{ level.label }}</option>
      }
    </select>
  `,
})
export class LevelSelect {
  /** The id of the list, which must be the only one on the page. */
  readonly fieldId = input.required<string>();
  readonly label = input.required<string>();
  readonly control = input.required<FormControl<ContentAccess>>();

  protected readonly levels = contentAccessLevels.map((value) => ({ value, label: labels[value] }));
}
