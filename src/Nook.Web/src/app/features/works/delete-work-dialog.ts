import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { WorksApi } from '../../api/works-api';
import { somethingWentWrong } from '../../core/messages';
import { failureOf } from '../../core/reloadable';

/** What the dialog is opened with. */
export interface DeleteWorkContext {
  readonly id: string;
  readonly title: string;
}

/** The dialog that asks whether to delete a work, and deletes it, with its files, when told to. */
@Component({
  selector: 'app-delete-work-dialog',
  imports: [HlmButtonImports, HlmDialogImports],
  host: { class: 'grid gap-6' },
  template: `
    <hlm-dialog-header>
      <h2 hlmDialogTitle>Delete this book?</h2>
      <p hlmDialogDescription>
        “{{ context.title }}” and its files will be deleted. This cannot be undone.
      </p>
    </hlm-dialog-header>
    @if (failed()) {
      <p class="text-destructive text-sm" role="alert">{{ somethingWentWrong }}</p>
    }
    <hlm-dialog-footer>
      <button hlmBtn variant="outline" type="button" (click)="cancel()">Cancel</button>
      <button hlmBtn variant="destructive" type="button" [disabled]="deleting()" (click)="remove()">
        Delete
      </button>
    </hlm-dialog-footer>
  `,
})
export class DeleteWorkDialog {
  protected readonly context = injectBrnDialogContext<DeleteWorkContext>();
  protected readonly somethingWentWrong = somethingWentWrong;
  private readonly dialog = inject<BrnDialogRef>(BrnDialogRef);
  private readonly worksApi = inject(WorksApi);
  private readonly router = inject(Router);

  protected readonly deleting = signal(false);
  protected readonly failed = signal(false);

  protected async remove(): Promise<void> {
    this.deleting.set(true);
    this.failed.set(false);
    try {
      await this.worksApi.delete(this.context.id);
    } catch (error) {
      // The work is gone, or the user can no longer see it. Either way it is no longer theirs to delete, and its
      // page has nothing to show them.
      if (failureOf(error) !== 'not-found') {
        this.failed.set(true);
        this.deleting.set(false);
        return;
      }
    }
    this.dialog.close();
    await this.router.navigateByUrl('/library');
  }

  protected cancel(): void {
    this.dialog.close();
  }
}
