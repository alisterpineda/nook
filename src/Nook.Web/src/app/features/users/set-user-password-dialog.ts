import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { UsersApi } from '../../api/users-api';
import { fieldErrorsOf } from '../../core/messages';
import { failureOf } from '../../core/reloadable';

/** What the dialog is opened with. */
export interface SetUserPasswordContext {
  readonly id: string;
  /** The name the user goes by, for the dialog to say whose password it sets. */
  readonly name: string;
  /** Called as the dialog closes with the list out of date: the password was set, or the user is no longer there. */
  readonly changed: () => void;
}

/** The dialog in which an admin gives another user a new password. */
@Component({
  selector: 'app-set-user-password-dialog',
  imports: [
    ReactiveFormsModule,
    HlmButtonImports,
    HlmDialogImports,
    HlmInputImports,
    HlmLabelImports,
  ],
  host: { class: 'grid gap-6' },
  template: `
    <hlm-dialog-header>
      <h2 hlmDialogTitle>Set a password</h2>
      <p hlmDialogDescription>
        Choose a new password for {{ context.name }}. Their sessions are signed out, and the old
        password stops working.
      </p>
    </hlm-dialog-header>
    <form class="grid gap-6" [formGroup]="form" (ngSubmit)="submit()">
      <div class="grid gap-2">
        <label hlmLabel for="set-user-password">Password</label>
        <input
          hlmInput
          id="set-user-password"
          type="password"
          autocomplete="new-password"
          formControlName="password"
          [forceInvalid]="passwordErrors().length > 0"
        />
        @for (message of passwordErrors(); track $index) {
          <p class="text-destructive text-sm" role="alert">{{ message }}</p>
        }
      </div>
      @for (message of otherErrors(); track $index) {
        <p class="text-destructive text-sm" role="alert">{{ message }}</p>
      }
      <hlm-dialog-footer>
        <button hlmBtn variant="outline" type="button" (click)="cancel()">Cancel</button>
        <button hlmBtn type="submit" [disabled]="submitting()">Set password</button>
      </hlm-dialog-footer>
    </form>
  `,
})
export class SetUserPasswordDialog {
  protected readonly context = injectBrnDialogContext<SetUserPasswordContext>();
  private readonly dialog = inject<BrnDialogRef>(BrnDialogRef);
  private readonly usersApi = inject(UsersApi);

  protected readonly form = inject(NonNullableFormBuilder).group({ password: '' });
  protected readonly submitting = signal(false);
  protected readonly passwordErrors = signal<string[]>([]);
  /** Messages that belong to no one field: a validation error keyed by something else, or any other failure. */
  protected readonly otherErrors = signal<string[]>([]);

  protected async submit(): Promise<void> {
    if (this.submitting()) {
      return;
    }
    this.showErrors({});
    this.submitting.set(true);
    try {
      await this.usersApi.setPassword(this.context.id, {
        password: this.form.getRawValue().password,
      });
    } catch (error) {
      if (failureOf(error) === 'not-found') {
        // The user is gone, so there is nothing to set a password on and the list is out of date.
        this.dialog.close();
        this.context.changed();
        return;
      }
      this.showErrors(fieldErrorsOf(error));
      this.submitting.set(false);
      return;
    }
    this.dialog.close();
    this.context.changed();
  }

  protected cancel(): void {
    this.dialog.close();
  }

  /** Shows each message by the field the API keys it by, `Password`, and any other above the buttons. */
  private showErrors(errors: Record<string, string[]>): void {
    const { Password = [], ...rest } = errors;
    this.passwordErrors.set(Password);
    this.otherErrors.set(Object.values(rest).flat());
  }
}
