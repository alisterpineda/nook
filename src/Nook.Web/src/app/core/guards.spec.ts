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
import { openRequests } from '../testing/open-requests';

type CurrentUserResponse = components['schemas']['CurrentUserResponse'];
type WorkDetailResponse = components['schemas']['WorkDetailResponse'];

describe('route guards', () => {
  const ada: CurrentUserResponse = {
    id: '0198a1b2-0000-7000-8000-000000000001',
    email: 'ada@example.com',
    role: 'admin',
  };
  const mel: CurrentUserResponse = {
    id: '0198a1b2-0000-7000-8000-000000000002',
    email: 'mel@example.com',
    role: 'member',
  };
  const gus: CurrentUserResponse = {
    id: '0198a1b2-0000-7000-8000-000000000003',
    email: 'gus@example.com',
    role: 'guest',
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

  async function answerSignedIn(user: CurrentUserResponse = ada): Promise<void> {
    (await nextRequest('/api/auth/me')).flush(user);
  }

  async function answerSetupRequired(required: boolean): Promise<void> {
    (await nextRequest('/api/setup')).flush({ required });
  }

  /** Answers the sidebar's request, which the signed-in pages make once they are shown. */
  async function answerSections(): Promise<void> {
    (await nextRequest('/api/sections')).flush([]);
  }

  /** Answers the works list's request, which it makes once it is shown. */
  async function answerWorks(): Promise<void> {
    (
      await vi.waitFor(() => httpTesting.expectOne((request) => request.url === '/api/works'))
    ).flush({ items: [], total: 0, offset: 0, limit: 50 });
  }

  /** Answers the work detail page's request for work 42, which it makes once it is shown. */
  async function answerWork(): Promise<void> {
    (await nextRequest('/api/works/42')).flush({
      id: '42',
      sectionId: '0198a1b2-0000-7000-8000-00000000000a',
      title: 'Dune',
      sortTitle: 'Dune',
      description: null,
      language: null,
      publicationDate: null,
      publisher: null,
      series: null,
      coverHash: null,
      contributors: [],
      tags: [],
      externalIds: [],
      items: [],
      addedAt: '2026-11-01T12:00:00Z',
      updatedAt: '2026-11-01T12:00:00Z',
    } satisfies WorkDetailResponse);
  }

  function heading(): string | undefined {
    return harness.routeNativeElement?.querySelector('h1')?.textContent?.trim();
  }

  it('a signed-out visitor to /library lands on /sign-in when a user exists', async () => {
    const navigating = harness.navigateByUrl('/library');
    await answerSignedOut();
    await answerSetupRequired(false);
    await navigating;

    expect(router.url).toBe('/sign-in');
    expect(heading()).toBe('Sign in');
  });

  it('a signed-out visitor to /library lands on /setup when no user exists', async () => {
    const navigating = harness.navigateByUrl('/library');
    await answerSignedOut();
    await answerSetupRequired(true);
    // The setup page's own guard asks again.
    await answerSetupRequired(true);
    await navigating;

    expect(router.url).toBe('/setup');
    expect(heading()).toBe('Set up Nook');
  });

  it('a signed-in user reaches /library', async () => {
    const navigating = harness.navigateByUrl('/library');
    await answerSignedIn();
    await answerSections();
    await answerWorks();
    await navigating;

    expect(router.url).toBe('/library');
    expect(heading()).toBe('Library');
  });

  it('a signed-in user reaches a work', async () => {
    const navigating = harness.navigateByUrl('/works/42');
    await answerSignedIn();
    await answerSections();
    await answerWork();
    await navigating;

    expect(router.url).toBe('/works/42');
    await vi.waitFor(() => expect(heading()).toBe('Dune'));
  });

  it('a signed-in user who opens /sign-in lands on /library', async () => {
    const navigating = harness.navigateByUrl('/sign-in');
    await answerSignedIn();
    await answerSections();
    await answerWorks();
    await navigating;

    expect(router.url).toBe('/library');
  });

  it('a signed-in user who opens /setup lands on /library', async () => {
    const navigating = harness.navigateByUrl('/setup');
    await answerSignedIn();
    await answerSections();
    await answerWorks();
    await navigating;

    expect(router.url).toBe('/library');
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
    const first = harness.navigateByUrl('/library');
    await answerSignedIn();
    await answerSections();
    await answerWorks();
    await first;

    const second = harness.navigateByUrl('/works/42');
    await answerWork();
    await second;

    httpTesting.expectNone('/api/auth/me');
    expect(router.url).toBe('/works/42');
  });

  it('an unknown path redirects to /library', async () => {
    const navigating = harness.navigateByUrl('/no/such/page');
    await answerSignedIn();
    await answerSections();
    await answerWorks();
    await navigating;

    expect(router.url).toBe('/library');
  });

  it('/ redirects to /library', async () => {
    const navigating = harness.navigateByUrl('/');
    await answerSignedIn();
    await answerSections();
    await answerWorks();
    await navigating;

    expect(router.url).toBe('/library');
  });

  it('adminGuard lets an admin through to /upload', async () => {
    const navigating = harness.navigateByUrl('/upload');
    await answerSignedIn(ada);
    // The sidebar and the upload page each ask for the sections.
    for (const request of await openRequests(httpTesting, '/api/sections', 2)) {
      request.flush([]);
    }
    await navigating;

    expect(router.url).toBe('/upload');
    await vi.waitFor(() => expect(heading()).toBe('Add books'));
  });

  it('adminGuard sends a member to the works list', async () => {
    const navigating = harness.navigateByUrl('/upload');
    await answerSignedIn(mel);
    await answerSections();
    await answerWorks();
    await navigating;

    expect(router.url).toBe('/library');
  });

  it('adminGuard sends a guest to the works list', async () => {
    const navigating = harness.navigateByUrl('/upload');
    await answerSignedIn(gus);
    await answerSections();
    await answerWorks();
    await navigating;

    expect(router.url).toBe('/library');
  });
});
