import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Provider, Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { components } from './api/schema';
import { routes } from './app.routes';
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
import { UploadPage } from './features/upload/upload-page';
import { UploadQueue } from './features/upload/upload-queue';
import { WorkDetailPage } from './features/works/work-detail-page';
import { WorksListPage } from './features/works/works-list-page';
import { SearchBox } from './shell/search-box';
import { Shell } from './shell/shell';
import { text } from './testing/element-text';
import { openRequests } from './testing/open-requests';

type CurrentUserResponse = components['schemas']['CurrentUserResponse'];
type SectionResponse = components['schemas']['SectionResponse'];
type PageOfPersonSummaryResponse = components['schemas']['PageOfPersonSummaryResponse'];
type PageOfWorkSummaryResponse = components['schemas']['PageOfWorkSummaryResponse'];
type PersonDetailResponse = components['schemas']['PersonDetailResponse'];
type PublicAccessResponse = components['schemas']['PublicAccessResponse'];
type UploadResponse = components['schemas']['UploadResponse'];
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
    addedBy: null,
  };

  /** What the API answers on each path for `visitor`, who is an admin unless they carry another role. */
  function answers(visitor: Visitor): Record<string, object> {
    return {
      '/api/setup': { required: visitor.setupRequired },
      '/api/auth/sign-in-options': { ssoName: null },
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

  it('an admin who opens /upload sees the upload page', async () => {
    expect(
      await open('/upload', { signedIn: true, setupRequired: false, role: 'admin' }, UploadPage),
    ).toEqual({ landedOn: '/upload', shown: true });
  });

  it('a contributor who opens /upload lands on the works list', async () => {
    expect(
      await open(
        '/upload',
        { signedIn: true, setupRequired: false, role: 'contributor' },
        WorksListPage,
      ),
    ).toEqual({ landedOn: '/library', shown: true });
  });

  it('a signed-out visitor who opens /upload lands on sign-in', async () => {
    expect(await open('/upload', { signedIn: false, setupRequired: false }, SignInPage)).toEqual({
      landedOn: '/sign-in',
      shown: true,
    });
  });

  it('an admin who opens /public-access sees the public access page', async () => {
    expect(
      await open(
        '/public-access',
        { signedIn: true, setupRequired: false, role: 'admin' },
        PublicAccessPage,
      ),
    ).toEqual({ landedOn: '/public-access', shown: true });
  });

  it('sends a viewer who asks for the public access page to the library', async () => {
    expect(
      await open(
        '/public-access',
        { signedIn: true, setupRequired: false, role: 'viewer' },
        WorksListPage,
      ),
    ).toEqual({ landedOn: '/library', shown: true });
  });

  it('sends a visitor who asks for the public access page to the library', async () => {
    expect(
      await open(
        '/public-access',
        { signedIn: false, setupRequired: false, publicAccess: true },
        WorksListPage,
      ),
    ).toEqual({ landedOn: '/library', shown: true });
  });

  it('the upload list is still there after visiting the works list and coming back', async () => {
    const books: SectionResponse = { id: '0198a1b2-0000-7000-8000-00000000000a', name: 'Books' };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    });
    const httpTesting = TestBed.inject(HttpTestingController);
    const harness = await RouterTestingHarness.create();

    const opening = harness.navigateByUrl('/upload');
    (await vi.waitFor(() => httpTesting.expectOne('/api/auth/me'))).flush(ada);
    // The sidebar and the upload page each ask for the sections.
    for (const request of await openRequests(httpTesting, '/api/sections', 2)) {
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

    const leaving = harness.navigateByUrl('/library');
    (
      await vi.waitFor(() => httpTesting.expectOne((request) => request.url === '/api/works'))
    ).flush(noWorks);
    await leaving;
    expect(TestBed.inject(Router).url).toBe('/library');

    const returning = harness.navigateByUrl('/upload');
    (await vi.waitFor(() => httpTesting.expectOne('/api/sections'))).flush([books]);
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
