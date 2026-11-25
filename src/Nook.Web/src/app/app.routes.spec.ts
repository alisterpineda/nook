import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Provider, Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { components } from './api/schema';
import { routes } from './app.routes';
import { provideFakeActivityHub } from './testing/fake-activity-hub';
import { AccountPage } from './features/account/account-page';
import { AuditLogPage } from './features/audit-log/audit-log-page';
import { JobsPage } from './features/jobs/jobs-page';
import { AuthorsListPage } from './features/people/authors-list-page';
import { PersonPage } from './features/people/person-page';
import { PublicAccessPage } from './features/public-access/public-access-page';
import { ReaderPage } from './features/reader/reader-page';
import {
  FakeReaderEngine,
  provideFakeReaderEngine,
} from './features/reader/testing/fake-reader-engine';
import { SetupPage } from './features/setup/setup-page';
import { SignInPage } from './features/sign-in/sign-in-page';
import { UsersPage } from './features/users/users-page';
import { WorkDetailPage } from './features/works/work-detail-page';
import { WorkEditPage } from './features/works/work-edit-page';
import { WorksListPage } from './features/works/works-list-page';
import { SearchBox } from './features/works/search-box';
import { CommandPalette } from './shell/command-palette';
import { Shell } from './shell/shell';

type CurrentUserResponse = components['schemas']['CurrentUserResponse'];
type PageOfPersonSummaryResponse = components['schemas']['PageOfPersonSummaryResponse'];
type PageOfWorkSummaryResponse = components['schemas']['PageOfWorkSummaryResponse'];
type PersonDetailResponse = components['schemas']['PersonDetailResponse'];
type PublicAccessResponse = components['schemas']['PublicAccessResponse'];
type BuiltInRole = components['schemas']['BuiltInRole'];
type WorkDetailResponse = components['schemas']['WorkDetailResponse'];

/**
 * Who opens a page: signed in or not, with the role they hold when signed in, on an instance set up or not, and with
 * public access on or off.
 */
interface Visitor {
  signedIn: boolean;
  setupRequired: boolean;
  role?: BuiltInRole;
  publicAccess?: boolean;
}

describe('routes', () => {
  const ada: CurrentUserResponse = {
    id: '0198a1b2-0000-7000-8000-000000000001',
    email: 'ada@example.com',
    role: 'admin',
    displayName: 'Admin User',
    canUpload: true,
  };
  const noWorks: PageOfWorkSummaryResponse = { items: [], total: 0, offset: 0, limit: 50 };
  const noPeople: PageOfPersonSummaryResponse = { items: [], total: 0, offset: 0, limit: 50 };
  const herbert: PersonDetailResponse = {
    id: '42',
    name: 'Frank Herbert',
    sortName: 'Herbert, Frank',
    roles: [],
  };
  const dune: WorkDetailResponse = {
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
    access: 'download',
    canEdit: true,
    addedBy: null,
  };

  /** What the API answers on each path for `visitor`, who is an admin unless they carry another role. */
  function answers(visitor: Visitor): Record<string, object> {
    return {
      '/api/setup': { required: visitor.setupRequired },
      '/api/auth/sign-in-options': { ssoName: null, passwordSignIn: true },
      '/api/access': { publicAccess: visitor.publicAccess ?? false, defaultArea: 'none' },
      '/api/sections': [],
      '/api/works': noWorks,
      '/api/works/42': dune,
      '/api/people': noPeople,
      '/api/people/42': herbert,
      '/api/public-access': {
        enabled: false,
        access: 'none',
        defaultAreaAccess: 'none',
        sections: [],
      } satisfies PublicAccessResponse,
      '/api/activity/uploads': [],
      '/api/version': { version: '0.0.0-dev+abc1234' },
      '/api/jobs': { items: [], total: 0, offset: 0, limit: 50 },
      '/api/audit-log': { items: [], total: 0, offset: 0, limit: 50 },
      '/api/audit-log/filters': { actors: [], actions: [] },
      '/api/account': {
        email: 'ada@example.com',
        name: 'Admin User',
        role: 'Viewer',
        hasPassword: true,
        hasSingleSignOn: false,
      },
      '/api/users': { items: [], total: 0, offset: 0, limit: 50 },
      ...(visitor.signedIn ? { '/api/auth/me': { ...ada, role: visitor.role ?? ada.role } } : {}),
    };
  }

  /**
   * Opens `url` as `visitor` with `providers` added, answering each request the guards and pages make along the way,
   * waits for the page of type `page` to be on screen, and says whether one of type `lookFor` is on screen too.
   */
  async function open<T>(
    url: string,
    visitor: Visitor,
    page: Type<T>,
    providers: Provider[] = [],
    lookFor: Type<unknown> = page,
  ): Promise<{ landedOn: string; shown: boolean }> {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideFakeActivityHub(),
        ...providers,
      ],
    });
    const httpTesting = TestBed.inject(HttpTestingController);
    const harness = await RouterTestingHarness.create();
    const byPath = answers(visitor);

    let settled = false;
    const navigating = harness.navigateByUrl(url).finally(() => (settled = true));
    const isShown = (type: Type<unknown> = page) =>
      harness.fixture.debugElement.query(By.directive(type)) !== null;
    await vi.waitFor(() => {
      for (const request of httpTesting.match(() => true)) {
        const body = byPath[request.request.url];
        if (body) {
          request.flush(body);
        } else {
          request.flush(null, { status: 401, statusText: 'Unauthorized' });
        }
      }
      harness.detectChanges();
      expect(settled && isShown()).toBe(true);
    });
    await navigating;
    httpTesting.verify();

    return { landedOn: TestBed.inject(Router).url, shown: isShown(lookFor) };
  }

  it('the screens are at /setup, /sign-in, /library and /works/:id', async () => {
    const firstVisitor = { signedIn: false, setupRequired: true };
    const signedOut = { signedIn: false, setupRequired: false };
    const signedIn = { signedIn: true, setupRequired: false };

    expect(await open('/setup', firstVisitor, SetupPage)).toEqual({
      landedOn: '/setup',
      shown: true,
    });
    expect(await open('/sign-in', signedOut, SignInPage)).toEqual({
      landedOn: '/sign-in',
      shown: true,
    });
    expect(await open('/library', signedIn, WorksListPage)).toEqual({
      landedOn: '/library',
      shown: true,
    });
    expect(await open('/works/42', signedIn, WorkDetailPage)).toEqual({
      landedOn: '/works/42',
      shown: true,
    });
  });

  it("a work's edit page is at /works/:id/edit", async () => {
    expect(
      await open('/works/42/edit', { signedIn: true, setupRequired: false }, WorkEditPage),
    ).toEqual({
      landedOn: '/works/42/edit',
      shown: true,
    });
  });

  it('the authors list is at /authors', async () => {
    expect(
      await open('/authors', { signedIn: true, setupRequired: false }, AuthorsListPage),
    ).toEqual({ landedOn: '/authors', shown: true });
  });

  it("a person's page is at /people/:id", async () => {
    expect(await open('/people/42', { signedIn: true, setupRequired: false }, PersonPage)).toEqual({
      landedOn: '/people/42',
      shown: true,
    });
  });

  it('shows the reader outside the shell to a signed-in visitor', async () => {
    const signedIn = { signedIn: true, setupRequired: false };
    const reader = '/works/42/read/0198a1b2-0000-7000-8000-0000000000f1';

    expect(
      await open(reader, signedIn, ReaderPage, [provideFakeReaderEngine(new FakeReaderEngine())]),
    ).toEqual({ landedOn: reader, shown: true });
    expect(
      await open(
        reader,
        signedIn,
        ReaderPage,
        [provideFakeReaderEngine(new FakeReaderEngine())],
        Shell,
      ),
    ).toEqual({ landedOn: reader, shown: false });
  });

  it('shows the reader with no search box', async () => {
    const signedIn = { signedIn: true, setupRequired: false };
    const reader = '/works/42/read/0198a1b2-0000-7000-8000-0000000000f1';

    expect(await open('/library', signedIn, WorksListPage, [], SearchBox)).toEqual({
      landedOn: '/library',
      shown: true,
    });
    expect(
      await open(
        reader,
        signedIn,
        ReaderPage,
        [provideFakeReaderEngine(new FakeReaderEngine())],
        SearchBox,
      ),
    ).toEqual({ landedOn: reader, shown: false });
  });

  it('shows the reader with no palette', async () => {
    const signedIn = { signedIn: true, setupRequired: false };
    const reader = '/works/42/read/0198a1b2-0000-7000-8000-0000000000f1';

    expect(await open('/library', signedIn, WorksListPage, [], CommandPalette)).toEqual({
      landedOn: '/library',
      shown: true,
    });
    expect(
      await open(
        reader,
        signedIn,
        ReaderPage,
        [provideFakeReaderEngine(new FakeReaderEngine())],
        CommandPalette,
      ),
    ).toEqual({ landedOn: reader, shown: false });
  });

  it('a visitor opens the reader when public access is on', async () => {
    const reader = '/works/42/read/0198a1b2-0000-7000-8000-0000000000f1';

    expect(
      await open(
        reader,
        { signedIn: false, setupRequired: false, publicAccess: true },
        ReaderPage,
        [provideFakeReaderEngine(new FakeReaderEngine())],
      ),
    ).toEqual({ landedOn: reader, shown: true });
  });

  it('sends a signed-out visitor from the reader to sign-in', async () => {
    expect(
      await open(
        '/works/42/read/0198a1b2-0000-7000-8000-0000000000f1',
        { signedIn: false, setupRequired: false },
        SignInPage,
        [provideFakeReaderEngine(new FakeReaderEngine())],
      ),
    ).toEqual({ landedOn: '/sign-in', shown: true });
  });

  it('sends /upload to the library', async () => {
    // No route is named `upload`, so the catch-all route sends it where it sends any unknown path.
    for (const role of ['admin', 'contributor'] as const) {
      expect(
        await open('/upload', { signedIn: true, setupRequired: false, role }, WorksListPage),
      ).toEqual({ landedOn: '/library', shown: true });
    }
    expect(await open('/upload', { signedIn: false, setupRequired: false }, SignInPage)).toEqual({
      landedOn: '/sign-in',
      shown: true,
    });
  });

  it('an admin who opens /admin/public-access sees the public access page', async () => {
    expect(
      await open(
        '/admin/public-access',
        { signedIn: true, setupRequired: false, role: 'admin' },
        PublicAccessPage,
      ),
    ).toEqual({ landedOn: '/admin/public-access', shown: true });
  });

  it('sends a viewer who asks for the public access page to the library', async () => {
    expect(
      await open(
        '/admin/public-access',
        { signedIn: true, setupRequired: false, role: 'viewer' },
        WorksListPage,
      ),
    ).toEqual({ landedOn: '/library', shown: true });
  });

  it('sends a visitor who asks for the public access page to the library', async () => {
    expect(
      await open(
        '/admin/public-access',
        { signedIn: false, setupRequired: false, publicAccess: true },
        WorksListPage,
      ),
    ).toEqual({ landedOn: '/library', shown: true });
  });

  it('keeps /admin/jobs and /admin/audit-log to admins', async () => {
    const pages: [string, Type<unknown>][] = [
      ['/admin/jobs', JobsPage],
      ['/admin/audit-log', AuditLogPage],
    ];
    for (const [url, page] of pages) {
      expect(
        await open(url, { signedIn: true, setupRequired: false, role: 'admin' }, page),
      ).toEqual({ landedOn: url, shown: true });
      for (const role of ['contributor', 'viewer'] as const) {
        expect(
          await open(url, { signedIn: true, setupRequired: false, role }, WorksListPage),
        ).toEqual({ landedOn: '/library', shown: true });
      }
    }
  });

  it('opens the Users page for an admin who asks for /admin', async () => {
    expect(
      await open('/admin', { signedIn: true, setupRequired: false, role: 'admin' }, UsersPage),
    ).toEqual({ landedOn: '/admin/users', shown: true });
  });

  it('keeps /admin/users to admins', async () => {
    expect(
      await open(
        '/admin/users',
        { signedIn: true, setupRequired: false, role: 'admin' },
        UsersPage,
      ),
    ).toEqual({ landedOn: '/admin/users', shown: true });
    for (const role of ['contributor', 'viewer'] as const) {
      expect(
        await open('/admin/users', { signedIn: true, setupRequired: false, role }, WorksListPage),
      ).toEqual({ landedOn: '/library', shown: true });
    }
    expect(
      await open(
        '/admin/users',
        { signedIn: false, setupRequired: false, publicAccess: true },
        WorksListPage,
      ),
    ).toEqual({ landedOn: '/library', shown: true });
  });

  it('sends a visitor who opens /account to sign-in while public access is on', async () => {
    expect(
      await open(
        '/account',
        { signedIn: false, setupRequired: false, publicAccess: true },
        SignInPage,
        [],
        AccountPage,
      ),
    ).toEqual({ landedOn: '/sign-in', shown: false });
  });

  it('shows /account to a signed-in viewer', async () => {
    expect(
      await open('/account', { signedIn: true, setupRequired: false, role: 'viewer' }, AccountPage),
    ).toEqual({ landedOn: '/account', shown: true });
  });
});
