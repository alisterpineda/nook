import { HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { components } from '../../api/schema';
import { somethingWentWrong } from '../../core/messages';
import { Session } from '../../core/session';
import { SsoButton } from '../sign-in/sso-button';

type ValidationProblemDetails = components['schemas']['ValidationProblemDetails'];

/** First-run setup: creates the first admin, whom the API signs in. */
@Component({
  selector: 'app-setup-page',
  imports: [
    ReactiveFormsModule,
    HlmButtonImports,
    HlmCardImports,
    HlmInputImports,
    HlmLabelImports,
    SsoButton,
  ],
  template: `
    <main class="flex min-h-svh items-center justify-center p-4">
      <section hlmCard class="w-full max-w-sm">
        <header hlmCardHeader>
          <h1 hlmCardTitle>Set up Nook</h1>
          <p hlmCardDescription>Create the first admin account.</p>
        </header>
        <form hlmCardContent class="grid gap-6" [formGroup]="form" (ngSubmit)="submit()">
          <div class="grid gap-2">
            <label hlmLabel for="setup-email">Email</label>
            <input
              hlmInput
              id="setup-email"
              type="email"
              autocomplete="email"
              formControlName="email"
              [forceInvalid]="emailErrors().length > 0"
            />
            @for (message of emailErrors(); track $index) {
              <p class="text-destructive text-sm" role="alert">{{ message }}</p>
            }
          </div>
          <div class="grid gap-2">
            <label hlmLabel for="setup-password">Password</label>
            <input
              hlmInput
              id="setup-password"
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
          <button hlmBtn type="submit" [disabled]="submitting()">Create admin</button>
        </form>
        <app-sso-button />
      </section>
    </main>
  `,
})
export class SetupPage {
  private readonly session = inject(Session);
  private readonly router = inject(Router);

  protected readonly form = inject(NonNullableFormBuilder).group({ email: '', password: '' });
  protected readonly submitting = signal(false);
  protected readonly emailErrors = signal<string[]>([]);
  protected readonly passwordErrors = signal<string[]>([]);
  /** Messages that belong to no one field: a validation error keyed by something else, or any other failure. */
  protected readonly otherErrors = signal<string[]>([]);

  protected async submit(): Promise<void> {
    if (this.submitting()) {
      return;
    }
    this.submitting.set(true);
    this.showErrors({});

    const { email, password } = this.form.getRawValue();
    let destination: string;
    try {
      await this.session.createFirstAdmin(email, password);
      destination = '/library';
    } catch (error) {
      if (!(error instanceof HttpErrorResponse) || error.status !== HttpStatusCode.Conflict) {
        this.showFailure(error);
        return;
      }
      // Someone else finished setup first, so there is an account to sign in to.
      destination = '/sign-in';
    } finally {
      this.submitting.set(false);
    }
    await this.router.navigateByUrl(destination);
  }

  private showFailure(error: unknown): void {
    if (error instanceof HttpErrorResponse && error.status === HttpStatusCode.BadRequest) {
      const problem = error.error as ValidationProblemDetails | null;
      const errors = problem?.errors ?? {};
      if (Object.values(errors).some((messages) => messages.length > 0)) {
        this.showErrors(errors);
        return;
      }
    }
    this.showErrors({ '': [somethingWentWrong] });
  }

  /** Shows each message under the field the API keys it by, `Email` or `Password`, and any other above the button. */
  private showErrors(errors: Record<string, string[]>): void {
    const { Email = [], Password = [], ...rest } = errors;
    this.emailErrors.set(Email);
    this.passwordErrors.set(Password);
    this.otherErrors.set(Object.values(rest).flat());
  }
}
