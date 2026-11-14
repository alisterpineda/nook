import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { components } from './api/schema';
import { routes } from './app.routes';
import { SetupPage } from './features/setup/setup-page';
import { SignInPage } from './features/sign-in/sign-in-page';
import { UploadPage } from './features/upload/upload-page';
import { UploadQueue } from './features/upload/upload-queue';
import { WorkDetailPage } from './features/works/work-detail-page';
import { WorksListPage } from './features/works/works-list-page';
import { text } from './testing/element-text';
import { openRequests } from './testing/open-requests';

type CurrentUserResponse = components['schemas']['CurrentUserResponse'];
type LibraryResponse = components['schemas']['LibraryResponse'];
type PageOfWorkSummaryResponse = components['schemas']['PageOfWorkSummaryResponse'];
type UploadResponse = components['schemas']['UploadResponse'];
type UserRole = components['schemas']['UserRole'];
type WorkDetailResponse = components['schemas']['WorkDetailResponse'];

/** Who opens a page: signed in or not, with the role they hold when signed in, on an instance set up or not. */
interface Visitor {
  signedIn: boolean;
  setupRequired: boolean;
  role?: UserRole;
}

describe('routes', () => {
  const ada: CurrentUserResponse = {
    id: '0198a1b2-0000-7000-8000-000000000001',
    email: 'ada@example.com',
    role: 'admin',
  };
  const noWorks: PageOfWorkSummaryResponse = { items: [], total: 0, offset: 0, limit: 50 };
  const dune: WorkDetailResponse = {
    id: '42',
    libraryId: '0198a1b2-0000-7000-8000-00000000000a',
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
  };

  /** What the API answers on each path for `visitor`, who is an admin unless they carry another role. */
  function answers(visitor: Visitor): Record<string, object> {
    return {
      '/api/setup': { required: visitor.setupRequired },
      '/api/libraries': [],
      '/api/works': noWorks,
      '/api/works/42': dune,
      ...(visitor.signedIn ? { '/api/auth/me': { ...ada, role: visitor.role ?? ada.role } } : {}),
    };
  }

  /**
   * Opens `url` as `visitor`, answering each request the guards and pages make along the way, and returns the page
   * of type `page` that ends up on screen, if there is one.
   */
  async function open<T>(
    url: string,
    visitor: Visitor,
    page: Type<T>,
  ): Promise<{ landedOn: string; shown: boolean }> {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    });
    const httpTesting = TestBed.inject(HttpTestingController);
    const harness = await RouterTestingHarness.create();
    const byPath = answers(visitor);

    let settled = false;
    const navigating = harness.navigateByUrl(url).finally(() => (settled = true));
    const isShown = () => harness.fixture.debugElement.query(By.directive(page)) !== null;
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

    return { landedOn: TestBed.inject(Router).url, shown: isShown() };
  }

  it('the screens are at /setup, /sign-in, /works and /works/:id', async () => {
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
    expect(await open('/works', signedIn, WorksListPage)).toEqual({
      landedOn: '/works',
      shown: true,
    });
    expect(await open('/works/42', signedIn, WorkDetailPage)).toEqual({
      landedOn: '/works/42',
      shown: true,
    });
  });

  it('an admin who opens /upload sees the upload page', async () => {
    expect(
      await open('/upload', { signedIn: true, setupRequired: false, role: 'admin' }, UploadPage),
    ).toEqual({ landedOn: '/upload', shown: true });
  });

  it('a member who opens /upload lands on the works list', async () => {
    expect(
      await open(
        '/upload',
        { signedIn: true, setupRequired: false, role: 'member' },
        WorksListPage,
      ),
    ).toEqual({ landedOn: '/works', shown: true });
  });

  it('a signed-out visitor who opens /upload lands on sign-in', async () => {
    expect(await open('/upload', { signedIn: false, setupRequired: false }, SignInPage)).toEqual({
      landedOn: '/sign-in',
      shown: true,
    });
  });

  it('the upload list is still there after visiting the works list and coming back', async () => {
    const books: LibraryResponse = { id: '0198a1b2-0000-7000-8000-00000000000a', name: 'Books' };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    });
    const httpTesting = TestBed.inject(HttpTestingController);
    const harness = await RouterTestingHarness.create();

    const opening = harness.navigateByUrl('/upload');
    (await vi.waitFor(() => httpTesting.expectOne('/api/auth/me'))).flush(ada);
    // The sidebar and the upload page each ask for the libraries.
    for (const request of await openRequests(httpTesting, '/api/libraries', 2)) {
      request.flush([books]);
    }
    await opening;
    const page = () => harness.routeNativeElement as HTMLElement;
    const input = await vi.waitFor(() => {
      harness.detectChanges();
      const found = page().querySelector<HTMLInputElement>('input[type="file"]');
      expect(found).not.toBeNull();
      return found!;
    });

    const dune = new File(['epub bytes'], 'dune.epub', { type: 'application/epub+zip' });
    Object.defineProperty(input, 'files', { value: [dune], configurable: true });
    input.dispatchEvent(new Event('change'));
    const upload = httpTesting.expectOne('/api/uploads');

    const leaving = harness.navigateByUrl('/works');
    (
      await vi.waitFor(() => httpTesting.expectOne((request) => request.url === '/api/works'))
    ).flush(noWorks);
    await leaving;
    expect(TestBed.inject(Router).url).toBe('/works');

    const returning = harness.navigateByUrl('/upload');
    (await vi.waitFor(() => httpTesting.expectOne('/api/libraries'))).flush([books]);
    await returning;

    /** Each row's file name and the status it shows. */
    const rows = () => {
      harness.detectChanges();
      return Array.from(page().querySelectorAll('ul[aria-label="Files"] > li'), (row) => [
        text(row.querySelector('[data-file-name]')),
        text(row.querySelector('[data-slot="badge"]')),
      ]);
    };
    await vi.waitFor(() => expect(rows()).toEqual([['dune.epub', 'Uploading']]));

    upload.flush({ jobId: '7' } satisfies UploadResponse);
    await vi.waitFor(() => expect(rows()).toEqual([['dune.epub', 'Ingesting']]));

    // Stops following the job, so no poll is left running after the test.
    TestBed.inject(UploadQueue).reset();
    httpTesting.verify();
  });
});
