import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { components } from '../../api/schema';
import { Session } from '../../core/session';
import { fieldLabelled } from '../../testing/field-labelled';
import { typeInto } from '../../testing/type-into';
import { linkLabelled } from '../../testing/link-labelled';
import { SetupPage } from './setup-page';

type CurrentUserResponse = components['schemas']['CurrentUserResponse'];
type SignInOptionsResponse = components['schemas']['SignInOptionsResponse'];
type ValidationProblemDetails = components['schemas']['ValidationProblemDetails'];

describe('SetupPage', () => {
  const ada: CurrentUserResponse = {
    id: '0198a1b2-0000-7000-8000-000000000001',
    email: 'ada@example.com',
    role: 'admin',
    displayName: 'Ada Admin',
    canUpload: true,
  };

  let fixture: ComponentFixture<SetupPage>;
  let page: HTMLElement;
  let httpTesting: HttpTestingController;
  let router: Router;
  let signInOptions: TestRequest;

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
    signInOptions = httpTesting.expectOne('/api/auth/sign-in-options');
  });

  afterEach(() => httpTesting.verify());

  async function submit(
    email: string,
    password: string,
    name?: string,
    confirmation: string = password,
  ): Promise<void> {
    if (name !== undefined) {
      typeInto(fieldLabelled(page, 'Your name'), name);
    }
    typeInto(fieldLabelled(page, 'Email'), email);
    typeInto(fieldLabelled(page, 'Password'), password);
    typeInto(fieldLabelled(page, 'Confirm password'), confirmation);
    const button = Array.from(page.querySelectorAll('button')).find(
      (candidate) => candidate.textContent?.trim() === 'Create admin',
    );
    button?.click();
    await fixture.whenStable();
  }

  async function answerSignInOptions(options: SignInOptionsResponse): Promise<void> {
    signInOptions.flush(options);
    await fixture.whenStable();
  }

  it('offers single sign-on when the API names one', async () => {
    await answerSignInOptions({ ssoName: 'Authentik', passwordSignIn: true });

    const link = await vi.waitFor(() => linkLabelled(page, 'Sign in with Authentik'));
    expect(link.getAttribute('href')).toBe('/api/auth/oidc/login');
    expect(fieldLabelled(page, 'Email')).toBeDefined();
  });

  describe('without single sign-on', () => {
    beforeEach(() => answerSignInOptions({ ssoName: null, passwordSignIn: true }));

    it('offers no single sign-on', () => {
      expect(page.querySelector('a')).toBeNull();
    });

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

    it('sends nothing when the two passwords differ', async () => {
      await submit('ada@example.com', 'correct horse battery', undefined, 'correct horse batery');

      await vi.waitFor(() =>
        expect(fieldLabelled(page, 'Confirm password').parentElement?.textContent).toContain(
          'The two passwords do not match.',
        ),
      );
      httpTesting.expectNone('/api/setup');
      expect(router.url).toBe('/');
    });

    it('creates the admin when the two passwords match', async () => {
      await submit('ada@example.com', 'correct horse battery');

      const request = await vi.waitFor(() => httpTesting.expectOne('/api/setup'));
      expect(request.request.body).toEqual({
        email: 'ada@example.com',
        password: 'correct horse battery',
      });
      expect(page.textContent).not.toContain('The two passwords do not match.');
      request.flush(ada);

      await vi.waitFor(() => expect(router.url).toBe('/library'));
    });

    it('sends the name with the email and password', async () => {
      await submit('ada@example.com', 'correct horse battery', 'Ada Admin');

      const request = await vi.waitFor(() => httpTesting.expectOne('/api/setup'));
      expect(request.request.body).toEqual({
        email: 'ada@example.com',
        password: 'correct horse battery',
        displayName: 'Ada Admin',
      });
      request.flush(ada);

      await vi.waitFor(() => expect(router.url).toBe('/library'));
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
        expect(fieldLabelled(page, 'Password').parentElement?.textContent).toContain(
          'Passwords must be at least 8 characters.',
        ),
      );
      expect(fieldLabelled(page, 'Email').parentElement?.textContent).not.toContain(
        'Passwords must',
      );
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

      await vi.waitFor(() =>
        expect(page.textContent).toContain('Something went wrong. Try again.'),
      );
      expect(router.url).toBe('/');
    });
  });
});
