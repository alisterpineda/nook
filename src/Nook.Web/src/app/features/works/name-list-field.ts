import { Component, input } from '@angular/core';
import { FormControl, FormArray, ReactiveFormsModule } from '@angular/forms';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';

let nextId = 0;

/**
 * A list of text inputs, one per name, that adds, removes and, when the order can change, moves names in `names`.
 * A name is free text, so it is never split on a comma.
 */
@Component({
  selector: 'app-name-list-field',
  imports: [ReactiveFormsModule, HlmButtonImports, HlmInputImports],
  template: `
    <fieldset class="grid gap-2">
      <legend class="mb-2 text-sm leading-none font-medium">{{ legend() }}</legend>
      @for (name of names().controls; track name; let index = $index; let last = $last) {
        <div
          role="group"
          class="flex flex-wrap items-center gap-2"
          [attr.aria-label]="rowLabel(index)"
        >
          <label class="sr-only" [for]="idPrefix + index">{{ rowLabel(index) }}</label>
          <input hlmInput class="min-w-0 flex-1" [id]="idPrefix + index" [formControl]="name" />
          @if (ordered()) {
            <button
              hlmBtn
              type="button"
              variant="outline"
              [disabled]="index === 0"
              (click)="move(index, -1)"
            >
              Move up
            </button>
            <button
              hlmBtn
              type="button"
              variant="outline"
              [disabled]="last"
              (click)="move(index, 1)"
            >
              Move down
            </button>
          }
          <button hlmBtn type="button" variant="outline" (click)="names().removeAt(index)">
            Remove
          </button>
        </div>
      }
      <div>
        <button hlmBtn type="button" variant="outline" (click)="add()">Add</button>
      </div>
    </fieldset>
  `,
})
export class NameListField {
  readonly names = input.required<FormArray<FormControl<string>>>();
  readonly legend = input.required<string>();
  /** What one name is called, such as "Author". */
  readonly label = input.required<string>();
  /** Whether the order of the names means something, which shows the buttons that change it. */
  readonly ordered = input(false);

  protected readonly idPrefix = `name-list-${nextId++}-`;

  protected rowLabel(index: number): string {
    return `${this.label()} ${index + 1}`;
  }

  protected add(): void {
    this.names().push(new FormControl('', { nonNullable: true }));
  }

  protected move(index: number, by: -1 | 1): void {
    const names = this.names();
    const control = names.at(index);
    names.removeAt(index);
    names.insert(index + by, control);
  }
}
