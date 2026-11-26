import { Component, computed, inject, signal } from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { UsersApi } from '../../api/users-api';
import { conflictReasonOf } from '../../core/messages';
import { failureOf } from '../../core/reloadable';

/** The roles an admin can give. */
export type AssignableRole = 'admin' | 'contributor' | 'viewer';

const roles: readonly { value: AssignableRole; label: string }[] = [
  { value: 'admin', label: 'Admin' },
  { value: 'contributor', label: 'Contributor' },
  { value: 'viewer', label: 'Viewer' },
];

/** Whether a role is one of the three an admin can give. */
export function isAssignableRole(role: string | null | undefined): role is AssignableRole {
  return roles.some((r) => r.value === role);
}

/** What the dialog is opened with. */
export interface ChangeUserRoleContext {
  readonly id: string;
  /** The name the user goes by, for the dialog to say whose role it changes. */
  readonly name: string;
  /** The user's role when it is one of the three an admin can give, and null for any other. */
  readonly role: AssignableRole | null;
  /** Called as the dialog closes with the list out of date: the role was changed, or the user is no longer there. */
  readonly changed: () => void;
}

/** The dialog in which an admin moves another user to a different role. */
@Component({
  selector: 'app-change-user-role-dialog',
  imports: [HlmButtonImports, HlmDialogImports, HlmInputImports, HlmLabelImports],
  host: { class: 'grid gap-6' },
  template: `
    <hlm-dialog-header>
      <h2 hlmDialogTitle>Change role</h2>
      <p hlmDialogDescription>Choose a role for {{ context.name }}.</p>
    </hlm-dialog-header>
    <div class="grid gap-2">
      <label hlmLabel for="change-user-role">Role</label>
      <select hlmInput id="change-user-role" (change)="choose($event)">
        @if (context.role === null) {
          <option value="" [selected]="chosen() === null">Choose a role</option>
        }
        @for (role of roles; track role.value) {
          <option [value]="role.value" [selected]="role.value === chosen()">
            {{ role.label }}
          </option>
        }
      </select>
    </div>
    @if (message()) {
      <p class="text-destructive text-sm" role="alert">{{ message() }}</p>
    }
    <hlm-dialog-footer>
      <button hlmBtn variant="outline" type="button" (click)="cancel()">Cancel</button>
      <button hlmBtn type="button" [disabled]="!canSave()" (click)="save()">Save</button>
    </hlm-dialog-footer>
  `,
})
export class ChangeUserRoleDialog {
  protected readonly context = injectBrnDialogContext<ChangeUserRoleContext>();
  protected readonly roles = roles;
  private readonly dialog = inject<BrnDialogRef>(BrnDialogRef);
  private readonly usersApi = inject(UsersApi);

  protected readonly chosen = signal<AssignableRole | null>(this.context.role);
  protected readonly submitting = signal(false);
  protected readonly message = signal('');
  protected readonly canSave = computed(() => this.chosen() !== null && !this.submitting());

  protected choose(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.chosen.set(roles.find((role) => role.value === value)?.value ?? null);
  }

  protected async save(): Promise<void> {
    const role = this.chosen();
    if (role === null || this.submitting()) {
      return;
    }
    this.message.set('');
    this.submitting.set(true);
    try {
      await this.usersApi.setRole(this.context.id, { role });
    } catch (error) {
      // A user who is gone has no role to change, and the list is out of date.
      if (failureOf(error) !== 'not-found') {
        this.message.set(conflictReasonOf(error));
        this.submitting.set(false);
        return;
      }
    }
    this.dialog.close();
    this.context.changed();
  }

  protected cancel(): void {
    this.dialog.close();
  }
}
