import { Component, inject, signal } from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { UsersApi } from '../../api/users-api';
import { conflictReasonOf } from '../../core/messages';
import { failureOf } from '../../core/reloadable';

/** What the dialog is opened with. */
export interface DeleteUserContext {
  readonly id: string;
  /** The name the user goes by. */
  readonly name: string;
  readonly email: string;
  /** Called as the dialog closes with the list out of date: the user was deleted, or was already gone. */
  readonly deleted: () => void;
}

/** The dialog that asks whether to delete a user, and deletes them when told to. */
@Component({
  selector: 'app-delete-user-dialog',
  imports: [HlmButtonImports, HlmDialogImports],
  host: { class: 'grid gap-6' },
  template: `
    <hlm-dialog-header>
      <h2 hlmDialogTitle>Delete this user?</h2>
      <p hlmDialogDescription>
        {{ context.name }} ({{ context.email }}) can no longer sign in, and their sessions are
        signed out. The books they added stay in the library, with no owner. This cannot be undone.
      </p>
    </hlm-dialog-header>
    @if (message()) {
      <p class="text-destructive text-sm" role="alert">{{ message() }}</p>
    }
    <hlm-dialog-footer>
      <button hlmBtn variant="outline" type="button" (click)="cancel()">Cancel</button>
      <button hlmBtn variant="destructive" type="button" [disabled]="deleting()" (click)="remove()">
        Delete
      </button>
    </hlm-dialog-footer>
  `,
})
export class DeleteUserDialog {
  protected readonly context = injectBrnDialogContext<DeleteUserContext>();
  private readonly dialog = inject<BrnDialogRef>(BrnDialogRef);
  private readonly usersApi = inject(UsersApi);

  protected readonly deleting = signal(false);
  protected readonly message = signal('');

  protected async remove(): Promise<void> {
    this.deleting.set(true);
    this.message.set('');
    try {
      await this.usersApi.delete(this.context.id);
    } catch (error) {
      // A user who is already gone is what the admin asked for, and the list is out of date.
      if (failureOf(error) !== 'not-found') {
        this.message.set(conflictReasonOf(error));
        this.deleting.set(false);
        return;
      }
    }
    this.dialog.close();
    this.context.deleted();
  }

  protected cancel(): void {
    this.dialog.close();
  }
}
