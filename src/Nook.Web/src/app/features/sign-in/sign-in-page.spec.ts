import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { components } from '../../api/schema';
import { Session } from '../../core/session';
import { SignInPage } from './sign-in-page';

type CurrentUserResponse = components['schemas']['CurrentUserResponse'];
type ProblemDetails = components['schemas']['ProblemDetails'];

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

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'works', children: [] }]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    fixture = TestBed.createComponent(SignInPage);
    page = fixture.nativeElement;
    await fixture.whenStable();
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
    return Array.from(page.querySelectorAll('[role="alert"]')).map(
      (alert) => alert.textContent?.trim() ?? '',
    );
  }

  it('signing in goes to /works', async () => {
    await submit('ada@example.com', 'correct horse battery');

    const request = await vi.waitFor(() => httpTesting.expectOne('/api/auth/login'));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      email: 'ada@example.com',
      password: 'correct horse battery',
    });
    request.flush(ada);

    await vi.waitFor(() => expect(router.url).toBe('/works'));
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

    await vi.waitFor(() => expect(alerts()).toEqual(['Too many attempts. Try again in a minute.']));
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
