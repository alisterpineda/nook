import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { components } from '../../api/schema';
import { Session } from '../../core/session';
import { linkLabelled } from '../../testing/link-labelled';
import { SignInPage } from './sign-in-page';

type CurrentUserResponse = components['schemas']['CurrentUserResponse'];
type ProblemDetails = components['schemas']['ProblemDetails'];
type SignInOptionsResponse = components['schemas']['SignInOptionsResponse'];

/** What each alert inside `element` says. */
function alertsIn(element: HTMLElement): string[] {
  return Array.from(element.querySelectorAll('[role="alert"]')).map(
    (alert) => alert.textContent?.trim() ?? '',
  );
}

describe('SignInPage', () => {
  const ada: CurrentUserResponse = {
    id: '0198a1b2-0000-7000-8000-000000000001',
    email: 'ada@example.com',
    role: 'admin',
  };

  let fixture: ComponentFixture<SignInPage>;
  let page: HTMLElement;
  let httpTesting: HttpTestingController;
  let router: Router;
  let signInOptions: TestRequest;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'library', children: [] }]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    fixture = TestBed.createComponent(SignInPage);
    page = fixture.nativeElement;
    await fixture.whenStable();
    signInOptions = httpTesting.expectOne('/api/auth/sign-in-options');
  });

  afterEach(() => httpTesting.verify());

  /** The form control a label names, found the way assistive technology finds it. */
  function fieldLabelled(text: string): HTMLInputElement {
    const label = Array.from(page.querySelectorAll('label')).find(
      (candidate) => candidate.textContent?.trim() === text,
    );
    const control = label?.control;
    if (!(control instanceof HTMLInputElement)) {
      throw new Error(`No input is labelled "${text}".`);
    }
    return control;
  }

  function type(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  async function submit(email: string, password: string): Promise<void> {
    type(fieldLabelled('Email'), email);
    type(fieldLabelled('Password'), password);
    const button = Array.from(page.querySelectorAll('button')).find(
      (candidate) => candidate.textContent?.trim() === 'Sign in',
    );
    button?.click();
    await fixture.whenStable();
  }

  function alerts(): string[] {
    return alertsIn(page);
  }

  async function answerSignInOptions(options: SignInOptionsResponse): Promise<void> {
    signInOptions.flush(options);
    await fixture.whenStable();
  }

  it('offers single sign-on when the API names one', async () => {
    await answerSignInOptions({ ssoName: 'Authentik' });

    const link = await vi.waitFor(() => linkLabelled(page, 'Sign in with Authentik'));
    expect(link.getAttribute('href')).toBe('/api/auth/oidc/login');
    expect(fieldLabelled('Email')).toBeDefined();
  });

  describe('without single sign-on', () => {
    beforeEach(() => answerSignInOptions({ ssoName: null }));

    it('offers no single sign-on', () => {
      expect(page.querySelector('a')).toBeNull();
    });

    it('signing in goes to /library', async () => {
      await submit('ada@example.com', 'correct horse battery');

      const request = await vi.waitFor(() => httpTesting.expectOne('/api/auth/login'));
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({
        email: 'ada@example.com',
        password: 'correct horse battery',
      });
      request.flush(ada);

      await vi.waitFor(() => expect(router.url).toBe('/library'));
      expect(TestBed.inject(Session).user()).toEqual(ada);
    });

    it("shows the API's message when the email or password is wrong", async () => {
      await submit('ada@example.com', 'wrong password');

      const problem: ProblemDetails = { title: 'The email or password is incorrect.', status: 401 };
      (await vi.waitFor(() => httpTesting.expectOne('/api/auth/login'))).flush(problem, {
        status: 401,
        statusText: 'Unauthorized',
      });

      await vi.waitFor(() => expect(alerts()).toEqual(['The email or password is incorrect.']));
      expect(router.url).toBe('/');
      expect(TestBed.inject(Session).user()).toBeNull();
    });

    it('asks for a valid email and a password without asking the API', async () => {
      await submit('ada', '');

      httpTesting.expectNone('/api/auth/login');
      expect(alerts()).toEqual(['Enter your email address.', 'Enter your password.']);

      type(fieldLabelled('Email'), 'ada@example.com');
      await fixture.whenStable();
      expect(alerts()).toEqual(['Enter your password.']);
    });

    it('shows a wait message when sign-in is rate limited', async () => {
      await submit('ada@example.com', 'correct horse battery');

      (await vi.waitFor(() => httpTesting.expectOne('/api/auth/login'))).flush(null, {
        status: 429,
        statusText: 'Too Many Requests',
      });

      await vi.waitFor(() =>
        expect(alerts()).toEqual(['Too many attempts. Try again in a minute.']),
      );
      expect(router.url).toBe('/');
    });

    it('shows a general message when sign-in fails for another reason', async () => {
      await submit('ada@example.com', 'correct horse battery');

      (await vi.waitFor(() => httpTesting.expectOne('/api/auth/login'))).flush(null, {
        status: 500,
        statusText: 'Internal Server Error',
      });

      await vi.waitFor(() => expect(alerts()).toEqual(['Something went wrong. Try again.']));
      expect(router.url).toBe('/');
    });

    it('disables the button while signing in', async () => {
      await submit('ada@example.com', 'correct horse battery');
      const request = await vi.waitFor(() => httpTesting.expectOne('/api/auth/login'));

      const button = page.querySelector('button');
      await vi.waitFor(() => expect(button?.disabled).toBe(true));

      request.flush(null, { status: 401, statusText: 'Unauthorized' });
      await vi.waitFor(() => expect(button?.disabled).toBe(false));
      expect(alerts()).toEqual(['Something went wrong. Try again.']);
    });
  });
});

describe('SignInPage after single sign-on', () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'sign-in', component: SignInPage },
          { path: '**', children: [] },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => httpTesting.verify());

  /** The alerts on a sign-in page newly opened at `url`, once it has its sign-in options. */
  async function alertsAt(url: string): Promise<string[]> {
    await harness.navigateByUrl('/');
    await harness.navigateByUrl(url, SignInPage);
    const options: SignInOptionsResponse = { ssoName: 'Authentik' };
    httpTesting.expectOne('/api/auth/sign-in-options').flush(options);
    await harness.fixture.whenStable();
    return alertsIn(harness.routeNativeElement as HTMLElement);
  }

  it('shows why single sign-on failed', async () => {
    expect(await alertsAt('/sign-in?error=sso-failed')).toEqual([
      'Single sign-on did not work. Try again.',
    ]);
    expect(await alertsAt('/sign-in?error=sso-no-email')).toEqual([
      'Your single sign-on account has no email address, and Nook needs one.',
    ]);
    expect(await alertsAt('/sign-in?error=sso-email-unverified')).toEqual([
      'A Nook account already uses this email address, and your single sign-on provider has not confirmed that the address is yours.',
    ]);
  });

  it('shows no message for an error it does not know', async () => {
    expect(await alertsAt('/sign-in?error=other')).toEqual([]);
    expect(await alertsAt('/sign-in')).toEqual([]);
  });
});
