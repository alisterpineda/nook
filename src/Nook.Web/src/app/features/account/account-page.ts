import { HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { of } from 'rxjs';
import { AccountApi } from '../../api/account-api';
import { components } from '../../api/schema';
import { passwordsDoNotMatch, somethingWentWrong, tooManyAttempts } from '../../core/messages';
import { reloadable } from '../../core/reloadable';
import { Session } from '../../core/session';
import { LoadFailed } from '../../ui/load-failed';
import { LoadingRegion } from '../../ui/loading';
import { pageHeading } from '../../ui/page-heading';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';

type AccountResponse = components['schemas']['AccountResponse'];
type ValidationProblemDetails = components['schemas']['ValidationProblemDetails'];

/** Where the account stands. */
type Account =
  { status: 'loading' } | { status: 'loaded'; account: AccountResponse } | { status: 'failed' };

/** The signed-in user's own account: who they are, read-only, and their password when Nook signs in with one. */
@Component({
  selector: 'app-account-page',
  imports: [
    ReactiveFormsModule,
    HlmButtonImports,
    HlmInputImports,
    HlmLabelImports,
    HlmSkeletonImports,
    LoadFailed,
    LoadingRegion,
  ],
  template: `
    <div class="mb-6">
      <h1 [class]="pageHeading">Account</h1>
    </div>
    @let current = state();
    @if (current.status === 'loading') {
      <app-loading class="flex flex-col gap-3">
        @for (width of skeletonWidths; track $index) {
          <div hlmSkeleton class="h-4" [style.width.rem]="width" aria-hidden="true"></div>
        }
      </app-loading>
    } @else if (current.status === 'failed') {
      <app-load-failed (retry)="reload()" />
    } @else {
      <dl class="grid max-w-xl grid-cols-[max-content_1fr] gap-x-6 gap-y-3 text-sm">
        <dt class="text-muted-foreground">Email</dt>
        <dd>{{ current.account.email }}</dd>
        <dt class="text-muted-foreground">Name</dt>
        <dd>{{ current.account.name }}</dd>
        <dt class="text-muted-foreground">Role</dt>
        <dd>{{ current.account.role }}</dd>
      </dl>
      @if (passwordSignIn()) {
        @let hasPassword = current.account.hasPassword || passwordAdded();
        <section class="mt-10 max-w-sm">
          <h2 class="mb-4 text-lg font-medium">
            {{ hasPassword ? 'Change your password' : 'Add a password' }}
          </h2>
          <form class="grid gap-6" [formGroup]="form" (ngSubmit)="submit(hasPassword)">
            @if (hasPassword) {
              <div class="grid gap-2">
                <label hlmLabel for="account-current-password">Current password</label>
                <input
                  hlmInput
                  id="account-current-password"
                  type="password"
                  autocomplete="current-password"
                  formControlName="currentPassword"
                  [forceInvalid]="currentErrors().length > 0"
                />
                @for (message of currentErrors(); track $index) {
                  <p class="text-destructive text-sm" role="alert">{{ message }}</p>
                }
              </div>
            }
            <div class="grid gap-2">
              <label hlmLabel for="account-new-password">New password</label>
              <input
                hlmInput
                id="account-new-password"
                type="password"
                autocomplete="new-password"
                formControlName="newPassword"
                [forceInvalid]="newErrors().length > 0"
              />
              @for (message of newErrors(); track $index) {
                <p class="text-destructive text-sm" role="alert">{{ message }}</p>
              }
            </div>
            <div class="grid gap-2">
              <label hlmLabel for="account-confirm-password">Confirm new password</label>
              <input
                hlmInput
                id="account-confirm-password"
                type="password"
                autocomplete="new-password"
                formControlName="confirmPassword"
                [forceInvalid]="confirmErrors().length > 0"
              />
              @for (message of confirmErrors(); track $index) {
                <p class="text-destructive text-sm" role="alert">{{ message }}</p>
              }
            </div>
            @for (message of otherErrors(); track $index) {
              <p class="text-destructive text-sm" role="alert">{{ message }}</p>
            }
            @if (changed()) {
              <p class="text-sm" role="status">{{ changed() }}</p>
            }
            <button hlmBtn type="submit" class="justify-self-start" [disabled]="submitting()">
              {{ hasPassword ? 'Change password' : 'Add password' }}
            </button>
          </form>
        </section>
      }
    }
  `,
})
export class AccountPage {
  protected readonly pageHeading = pageHeading;
  private readonly accountApi = inject(AccountApi);
  private readonly session = inject(Session);

  protected readonly form = inject(NonNullableFormBuilder).group({
    currentPassword: '',
    newPassword: '',
    confirmPassword: '',
  });
  protected readonly submitting = signal(false);
  protected readonly currentErrors = signal<string[]>([]);
  protected readonly newErrors = signal<string[]>([]);
  protected readonly confirmErrors = signal<string[]>([]);
  /** Messages that belong to no one field: a validation error keyed by something else, or any other failure. */
  protected readonly otherErrors = signal<string[]>([]);
  /** What to tell the user once the password is saved, empty until then. */
  protected readonly changed = signal('');
  /** Whether the API accepted a password for an account that had none, so the form now asks for it. */
  protected readonly passwordAdded = signal(false);
  /** Whether Nook signs in with a password, which is when the account has one to manage. False until known. */
  protected readonly passwordSignIn = signal(false);

  /** How wide, in rem, each line standing in for a detail is while the account loads. */
  protected readonly skeletonWidths = [24, 20, 16];

  private readonly loading = reloadable(of(undefined), () => this.load());

  protected readonly state = toSignal(this.loading.state$, { requireSync: true });

  /** Asks again for what failed to load. */
  protected readonly reload = this.loading.reload;

  protected async submit(hasPassword: boolean): Promise<void> {
    if (this.submitting()) {
      return;
    }
    this.changed.set('');
    this.showErrors({});
    const { currentPassword, newPassword, confirmPassword } = this.form.getRawValue();
    if (newPassword !== confirmPassword) {
      this.confirmErrors.set([passwordsDoNotMatch]);
      return;
    }
    this.submitting.set(true);
    try {
      await this.accountApi.setPassword(newPassword, hasPassword ? currentPassword : null);
    } catch (error) {
      this.showFailure(error);
      return;
    } finally {
      this.submitting.set(false);
    }
    this.form.reset();
    this.changed.set(hasPassword ? 'Your password was changed.' : 'Your password was added.');
    this.passwordAdded.set(true);
  }

  private showFailure(error: unknown): void {
    if (error instanceof HttpErrorResponse) {
      if (error.status === HttpStatusCode.TooManyRequests) {
        this.showErrors({ '': [tooManyAttempts] });
        return;
      }
      const errors = (error.error as ValidationProblemDetails | null)?.errors ?? {};
      if (
        error.status === HttpStatusCode.BadRequest &&
        Object.values(errors).some((messages) => messages.length > 0)
      ) {
        this.showErrors(errors);
        return;
      }
    }
    this.showErrors({ '': [somethingWentWrong] });
  }

  /** Shows each message under the field the API keys it by, `CurrentPassword` or `NewPassword`, and any other above the button. */
  private showErrors(errors: Record<string, string[]>): void {
    const { CurrentPassword = [], NewPassword = [], ...rest } = errors;
    this.currentErrors.set(CurrentPassword);
    this.newErrors.set(NewPassword);
    this.confirmErrors.set([]);
    this.otherErrors.set(Object.values(rest).flat());
  }

  private async load(): Promise<Account> {
    try {
      // The sign-in options are part of the load, so a failure to read them offers the retry and hides no form.
      const [account, options] = await Promise.all([
        this.accountApi.get(),
        this.session.signInOptions(),
      ]);
      this.passwordSignIn.set(options.passwordSignIn);
      return { status: 'loaded', account };
    } catch {
      return { status: 'failed' };
    }
  }
}
