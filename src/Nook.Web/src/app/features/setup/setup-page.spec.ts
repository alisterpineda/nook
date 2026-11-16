import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { components } from '../../api/schema';
import { Session } from '../../core/session';
import { SetupPage } from './setup-page';

type CurrentUserResponse = components['schemas']['CurrentUserResponse'];
type ValidationProblemDetails = components['schemas']['ValidationProblemDetails'];

describe('SetupPage', () => {
  const ada: CurrentUserResponse = {
    id: '0198a1b2-0000-7000-8000-000000000001',
    email: 'ada@example.com',
    role: 'admin',
  };

  let fixture: ComponentFixture<SetupPage>;
  let page: HTMLElement;
  let httpTesting: HttpTestingController;
  let router: Router;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'library', children: [] },
          { path: 'sign-in', children: [] },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    fixture = TestBed.createComponent(SetupPage);
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
      (candidate) => candidate.textContent?.trim() === 'Create admin',
    );
    button?.click();
    await fixture.whenStable();
  }

  it('submitting creates the first admin and goes to /library', async () => {
    await submit('ada@example.com', 'correct horse battery');

    const request = await vi.waitFor(() => httpTesting.expectOne('/api/setup'));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      email: 'ada@example.com',
      password: 'correct horse battery',
    });
    request.flush(ada);

    await vi.waitFor(() => expect(router.url).toBe('/library'));
    expect(TestBed.inject(Session).user()).toEqual(ada);
  });

  it("shows the API's message under the password field on a validation error", async () => {
    await submit('ada@example.com', 'short');

    const problem: ValidationProblemDetails = {
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: { Password: ['Passwords must be at least 8 characters.'] },
    };
    (await vi.waitFor(() => httpTesting.expectOne('/api/setup'))).flush(problem, {
      status: 400,
      statusText: 'Bad Request',
    });

    await vi.waitFor(() =>
      expect(fieldLabelled('Password').parentElement?.textContent).toContain(
        'Passwords must be at least 8 characters.',
      ),
    );
    expect(fieldLabelled('Email').parentElement?.textContent).not.toContain('Passwords must');
    expect(router.url).toBe('/');
  });

  it('goes to /sign-in when Nook is already set up', async () => {
    await submit('ada@example.com', 'correct horse battery');

    (await vi.waitFor(() => httpTesting.expectOne('/api/setup'))).flush(
      { title: 'Nook is already set up.', status: 409 },
      { status: 409, statusText: 'Conflict' },
    );

    await vi.waitFor(() => expect(router.url).toBe('/sign-in'));
    expect(TestBed.inject(Session).user()).toBeNull();
  });

  it('shows a general message when setup fails for another reason', async () => {
    await submit('ada@example.com', 'correct horse battery');

    (await vi.waitFor(() => httpTesting.expectOne('/api/setup'))).flush(null, {
      status: 500,
      statusText: 'Internal Server Error',
    });

    await vi.waitFor(() => expect(page.textContent).toContain('Something went wrong. Try again.'));
    expect(router.url).toBe('/');
  });
});
