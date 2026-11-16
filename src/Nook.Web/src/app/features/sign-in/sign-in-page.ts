import { HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { components } from '../../api/schema';
import { somethingWentWrong } from '../../core/messages';
import { Session } from '../../core/session';
import { SsoButton } from './sso-button';

type ProblemDetails = components['schemas']['ProblemDetails'];

const tooManyAttempts = 'Too many attempts. Try again in a minute.';

/** What the API's single sign-on means by each `error` it sends back to this page. */
const singleSignOnFailures: Readonly<Record<string, string>> = {
  'sso-failed': 'Single sign-on did not work. Try again.',
  'sso-no-email': 'Your single sign-on account has no email address, and Nook needs one.',
  'sso-email-unverified':
    'A Nook account already uses this email address, and your single sign-on provider has not confirmed that the address is yours.',
  'sso-setup-required':
    'Nook has no admin yet. Set up Nook first, or use a single sign-on account that is an admin.',
};

/** Signs in with an email and password. */
@Component({
  selector: 'app-sign-in-page',
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
          <h1 hlmCardTitle>Sign in</h1>
          <p hlmCardDescription>Sign in to Nook with your email and password.</p>
        </header>
        <form hlmCardContent class="grid gap-6" [formGroup]="form" (ngSubmit)="submit()">
          <div class="grid gap-2">
            <label hlmLabel for="sign-in-email">Email</label>
            <input
              hlmInput
              id="sign-in-email"
              type="email"
              autocomplete="email"
              formControlName="email"
              [forceInvalid]="showsInvalid('email')"
            />
            @if (showsInvalid('email')) {
              <p class="text-destructive text-sm" role="alert">Enter your email address.</p>
            }
          </div>
          <div class="grid gap-2">
            <label hlmLabel for="sign-in-password">Password</label>
            <input
              hlmInput
              id="sign-in-password"
              type="password"
              autocomplete="current-password"
              formControlName="password"
              [forceInvalid]="showsInvalid('password')"
            />
            @if (showsInvalid('password')) {
              <p class="text-destructive text-sm" role="alert">Enter your password.</p>
            }
          </div>
          @if (error(); as message) {
            <p class="text-destructive text-sm" role="alert">{{ message }}</p>
          }
          <button hlmBtn type="submit" [disabled]="submitting()">Sign in</button>
        </form>
        <app-sso-button />
      </section>
    </main>
  `,
})
export class SignInPage {
  private readonly session = inject(Session);
  private readonly router = inject(Router);

  // The API refuses these with a 400, so they are caught here before a request spends a sign-in attempt.
  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });
  private readonly attempted = signal(false);
  protected readonly submitting = signal(false);
  // Single sign-on comes back here with why it failed; a password attempt replaces that message.
  protected readonly error = signal<string | null>(
    singleSignOnFailure(inject(ActivatedRoute).snapshot.queryParamMap.get('error')),
  );

  /** Whether to mark a field wrong: only once the user has tried to sign in with it. */
  protected showsInvalid(field: 'email' | 'password'): boolean {
    return this.attempted() && this.form.controls[field].invalid;
  }

  protected async submit(): Promise<void> {
    if (this.submitting()) {
      return;
    }
    this.attempted.set(true);
    this.error.set(null);
    if (this.form.invalid) {
      return;
    }
    this.submitting.set(true);

    const { email, password } = this.form.getRawValue();
    try {
      await this.session.signIn(email, password);
    } catch (error) {
      this.error.set(failureMessage(error));
      return;
    } finally {
      this.submitting.set(false);
    }
    await this.router.navigateByUrl('/library');
  }
}

/** The message for a single sign-on failure code, or null for no code or one this page does not know. */
function singleSignOnFailure(code: string | null): string | null {
  return code !== null && Object.hasOwn(singleSignOnFailures, code)
    ? singleSignOnFailures[code]
    : null;
}

/** The API's own words for a wrong email or password, and fixed words for anything else. */
function failureMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === HttpStatusCode.TooManyRequests) {
      return tooManyAttempts;
    }
    if (error.status === HttpStatusCode.Unauthorized) {
      const problem = error.error as ProblemDetails | null;
      if (problem?.title) {
        return problem.title;
      }
    }
  }
  return somethingWentWrong;
}
