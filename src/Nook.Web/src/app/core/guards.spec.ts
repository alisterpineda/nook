import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { components } from '../api/schema';
import { routes } from '../app.routes';

type CurrentUserResponse = components['schemas']['CurrentUserResponse'];

describe('route guards', () => {
  const ada: CurrentUserResponse = {
    id: '0198a1b2-0000-7000-8000-000000000001',
    email: 'ada@example.com',
    role: 'admin',
  };

  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let router: Router;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => httpTesting.verify());

  /** Waits for the guards to send the request, since they send it after the navigation has started. */
  function nextRequest(url: string): Promise<TestRequest> {
    return vi.waitFor(() => httpTesting.expectOne(url));
  }

  async function answerSignedOut(): Promise<void> {
    (await nextRequest('/api/auth/me')).flush(null, { status: 401, statusText: 'Unauthorized' });
  }

  async function answerSignedIn(): Promise<void> {
    (await nextRequest('/api/auth/me')).flush(ada);
  }

  async function answerSetupRequired(required: boolean): Promise<void> {
    (await nextRequest('/api/setup')).flush({ required });
  }

  /** Answers the sidebar's request, which the signed-in pages make once they are shown. */
  async function answerLibraries(): Promise<void> {
    (await nextRequest('/api/libraries')).flush([]);
  }

  function heading(): string | undefined {
    return harness.routeNativeElement?.querySelector('h1')?.textContent?.trim();
  }

  it('a signed-out visitor to /works lands on /sign-in when a user exists', async () => {
    const navigating = harness.navigateByUrl('/works');
    await answerSignedOut();
    await answerSetupRequired(false);
    await navigating;

    expect(router.url).toBe('/sign-in');
    expect(heading()).toBe('Sign in');
  });

  it('a signed-out visitor to /works lands on /setup when no user exists', async () => {
    const navigating = harness.navigateByUrl('/works');
    await answerSignedOut();
    await answerSetupRequired(true);
    // The setup page's own guard asks again.
    await answerSetupRequired(true);
    await navigating;

    expect(router.url).toBe('/setup');
    expect(heading()).toBe('Set up Nook');
  });

  it('a signed-in user reaches /works', async () => {
    const navigating = harness.navigateByUrl('/works');
    await answerSignedIn();
    await answerLibraries();
    await navigating;

    expect(router.url).toBe('/works');
    expect(heading()).toBe('Works');
  });

  it('a signed-in user reaches a work', async () => {
    const navigating = harness.navigateByUrl('/works/42');
    await answerSignedIn();
    await answerLibraries();
    await navigating;

    expect(router.url).toBe('/works/42');
    expect(heading()).toBe('Work');
  });

  it('a signed-in user who opens /sign-in lands on /works', async () => {
    const navigating = harness.navigateByUrl('/sign-in');
    await answerSignedIn();
    await answerLibraries();
    await navigating;

    expect(router.url).toBe('/works');
  });

  it('a signed-in user who opens /setup lands on /works', async () => {
    const navigating = harness.navigateByUrl('/setup');
    await answerSignedIn();
    await answerLibraries();
    await navigating;

    expect(router.url).toBe('/works');
  });

  it('a signed-out visitor reaches /sign-in', async () => {
    const navigating = harness.navigateByUrl('/sign-in');
    await answerSignedOut();
    await navigating;

    expect(router.url).toBe('/sign-in');
    expect(heading()).toBe('Sign in');
  });

  it('a signed-out visitor to /setup lands on /sign-in when a user exists', async () => {
    const navigating = harness.navigateByUrl('/setup');
    await answerSignedOut();
    await answerSetupRequired(false);
    await navigating;

    expect(router.url).toBe('/sign-in');
  });

  it('a signed-out visitor reaches /setup when no user exists', async () => {
    const navigating = harness.navigateByUrl('/setup');
    await answerSignedOut();
    await answerSetupRequired(true);
    await navigating;

    expect(router.url).toBe('/setup');
  });

  it('asks the API for the session only once across navigations', async () => {
    const first = harness.navigateByUrl('/works');
    await answerSignedIn();
    await answerLibraries();
    await first;

    await harness.navigateByUrl('/works/42');

    httpTesting.expectNone('/api/auth/me');
    expect(router.url).toBe('/works/42');
  });

  it('an unknown path redirects to /works', async () => {
    const navigating = harness.navigateByUrl('/no/such/page');
    await answerSignedIn();
    await answerLibraries();
    await navigating;

    expect(router.url).toBe('/works');
  });

  it('/ redirects to /works', async () => {
    const navigating = harness.navigateByUrl('/');
    await answerSignedIn();
    await answerLibraries();
    await navigating;

    expect(router.url).toBe('/works');
  });
});
