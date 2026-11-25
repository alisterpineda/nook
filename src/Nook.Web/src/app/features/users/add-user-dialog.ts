import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { UsersApi } from '../../api/users-api';
import { components } from '../../api/schema';
import { fieldErrorsOf } from '../../core/messages';

type BuiltInRole = components['schemas']['BuiltInRole'];

/** What the dialog is opened with. */
export interface AddUserContext {
  /** Called once the API has created the account, as the dialog closes. */
  readonly added: () => void;
}

const roles: readonly { value: BuiltInRole; label: string }[] = [
  { value: 'admin', label: 'Admin' },
  { value: 'contributor', label: 'Contributor' },
  { value: 'viewer', label: 'Viewer' },
];

/** The dialog in which an admin gives a new person an email, a name, a role and a first password. */
@Component({
  selector: 'app-add-user-dialog',
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
      <h2 hlmDialogTitle>Add a user</h2>
      <p hlmDialogDescription>
        They sign in with this email and password, and can change the password on their Account
        page.
      </p>
    </hlm-dialog-header>
    <form class="grid gap-6" [formGroup]="form" (ngSubmit)="submit()">
      <div class="grid gap-2">
        <label hlmLabel for="add-user-email">Email</label>
        <input
          hlmInput
          id="add-user-email"
          type="email"
          autocomplete="off"
          formControlName="email"
          [forceInvalid]="emailErrors().length > 0"
        />
        @for (message of emailErrors(); track $index) {
          <p class="text-destructive text-sm" role="alert">{{ message }}</p>
        }
      </div>
      <div class="grid gap-2">
        <label hlmLabel for="add-user-name">Name</label>
        <input hlmInput id="add-user-name" autocomplete="off" formControlName="name" />
      </div>
      <div class="grid gap-2">
        <label hlmLabel for="add-user-role">Role</label>
        <select
          hlmInput
          id="add-user-role"
          formControlName="role"
          [forceInvalid]="roleErrors().length > 0"
        >
          @for (role of roles; track role.value) {
            <option [value]="role.value">{{ role.label }}</option>
          }
        </select>
        @for (message of roleErrors(); track $index) {
          <p class="text-destructive text-sm" role="alert">{{ message }}</p>
        }
      </div>
      <div class="grid gap-2">
        <label hlmLabel for="add-user-password">Password</label>
        <input
          hlmInput
          id="add-user-password"
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
        <button hlmBtn type="submit" [disabled]="submitting()">Add user</button>
      </hlm-dialog-footer>
    </form>
  `,
})
export class AddUserDialog {
  protected readonly roles = roles;
  private readonly context = injectBrnDialogContext<AddUserContext>();
  private readonly dialog = inject<BrnDialogRef>(BrnDialogRef);
  private readonly usersApi = inject(UsersApi);

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: '',
    name: '',
    role: 'viewer' as BuiltInRole,
    password: '',
  });
  protected readonly submitting = signal(false);
  protected readonly emailErrors = signal<string[]>([]);
  protected readonly roleErrors = signal<string[]>([]);
  protected readonly passwordErrors = signal<string[]>([]);
  /** Messages that belong to no one field: a validation error keyed by something else, or any other failure. */
  protected readonly otherErrors = signal<string[]>([]);

  protected async submit(): Promise<void> {
    if (this.submitting()) {
      return;
    }
    this.showErrors({});
    const { email, name, role, password } = this.form.getRawValue();
    this.submitting.set(true);
    try {
      await this.usersApi.add({
        email: email.trim(),
        displayName: name.trim() || null,
        role,
        password,
      });
    } catch (error) {
      this.showErrors(fieldErrorsOf(error));
      this.submitting.set(false);
      return;
    }
    this.dialog.close();
    this.context.added();
  }

  protected cancel(): void {
    this.dialog.close();
  }

  /** Shows each message by the field the API keys it by, `Email`, `Role` or `Password`, and any other above the buttons. */
  private showErrors(errors: Record<string, string[]>): void {
    const { Email = [], Role = [], Password = [], ...rest } = errors;
    this.emailErrors.set(Email);
    this.roleErrors.set(Role);
    this.passwordErrors.set(Password);
    this.otherErrors.set(Object.values(rest).flat());
  }
}
