import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { components } from '../api/schema';
import { Session } from '../core/session';
import { UploadQueue } from '../features/upload/upload-queue';
import { Shell } from './shell';

type CurrentUserResponse = components['schemas']['CurrentUserResponse'];
type LibraryResponse = components['schemas']['LibraryResponse'];

@Component({ template: '<h1>Works</h1>' })
class WorksStub {}

@Component({ template: '<h1>Add books</h1>' })
class UploadStub {}

@Component({ template: '<h1>Sign in</h1>' })
class SignInStub {}

describe('Shell', () => {
  const ada: CurrentUserResponse = {
    id: '0198a1b2-0000-7000-8000-000000000001',
    email: 'ada@example.com',
    role: 'admin',
  };
  const grace: CurrentUserResponse = {
    id: '0198a1b2-0000-7000-8000-000000000002',
    email: 'grace@example.com',
    role: 'member',
  };
  const guest: CurrentUserResponse = {
    id: '0198a1b2-0000-7000-8000-000000000003',
    email: 'guest@example.com',
    role: 'guest',
  };
  const books: LibraryResponse = { id: '0198a1b2-0000-7000-8000-00000000000a', name: 'Books' };
  const comics: LibraryResponse = { id: '0198a1b2-0000-7000-8000-00000000000b', name: 'Comics' };

  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let router: Router;
  let ignoreCancelled: boolean;

  beforeEach(async () => {
    ignoreCancelled = false;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'sign-in', component: SignInStub },
          {
            path: '',
            component: Shell,
            children: [
              { path: 'works', component: WorksStub },
              { path: 'upload', component: UploadStub },
            ],
          },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);

    await signInAs(ada);

    harness = await RouterTestingHarness.create();
  });

  afterEach(() => {
    httpTesting.verify({ ignoreCancelled });
    // The sidebar remembers whether it is open in a cookie, which would carry over to the next test.
    document.cookie = 'sidebar_state=; path=/; max-age=0';
  });

  /** Loads the session with `user` signed in. */
  async function signInAs(user: CurrentUserResponse): Promise<void> {
    const loading = TestBed.inject(Session).load();
    httpTesting.expectOne('/api/auth/me').flush(user);
    await loading;
  }

  /** Opens the shell at `url`, answering its request for the libraries with `answer`. */
  async function openShell(
    url: string,
    answer: (libraries: TestRequest) => void = (request) => request.flush([books, comics]),
  ): Promise<HTMLElement> {
    const navigating = harness.navigateByUrl(url);
    answer(await vi.waitFor(() => httpTesting.expectOne('/api/libraries')));
    await navigating;
    await harness.fixture.whenStable();
    return harness.fixture.nativeElement;
  }

  /** The links of the sidebar group labelled `group`. */
  function groupLinks(shell: HTMLElement, group: string): HTMLAnchorElement[] {
    return Array.from(shell.querySelectorAll<HTMLAnchorElement>(`nav[aria-label="${group}"] a`));
  }

  function sidebarLinks(shell: HTMLElement): HTMLAnchorElement[] {
    return groupLinks(shell, 'Libraries');
  }

  function addBooks(shell: HTMLElement): HTMLAnchorElement | undefined {
    return Array.from(shell.querySelectorAll<HTMLAnchorElement>('nav a')).find(
      (link) => link.textContent?.trim() === 'Add books',
    );
  }

  function linkNamed(shell: HTMLElement, name: string): HTMLAnchorElement {
    const link = sidebarLinks(shell).find((candidate) => candidate.textContent?.trim() === name);
    if (!link) {
      throw new Error(`The sidebar has no link named "${name}".`);
    }
    return link;
  }

  function selectedLinks(shell: HTMLElement): string[] {
    return sidebarLinks(shell)
      .filter((link) => link.getAttribute('aria-current') === 'page')
      .map((link) => link.textContent?.trim() ?? '');
  }

  function buttonNamed(root: ParentNode, name: string): HTMLElement {
    const button = Array.from(root.querySelectorAll<HTMLElement>('button')).find(
      (candidate) => candidate.textContent?.trim() === name,
    );
    if (!button) {
      throw new Error(`No button is named "${name}".`);
    }
    return button;
  }

  async function signOutFromTheFooterMenu(shell: HTMLElement): Promise<void> {
    buttonNamed(shell, ada.email).click();
    await harness.fixture.whenStable();
    const signOut = await vi.waitFor(() => {
      const item = Array.from(document.querySelectorAll<HTMLElement>('[role="menuitem"]')).find(
        (candidate) => candidate.textContent?.trim() === 'Sign out',
      );
      if (!item) {
        throw new Error('The menu has no "Sign out" item.');
      }
      return item;
    });
    signOut.click();
  }

  it('lists All works first, then the libraries', async () => {
    const shell = await openShell('/works');

    expect(sidebarLinks(shell).map((link) => link.textContent?.trim())).toEqual([
      'All works',
      'Books',
      'Comics',
    ]);
    expect(linkNamed(shell, 'All works').getAttribute('href')).toBe('/works');
    expect(linkNamed(shell, 'Books').getAttribute('href')).toBe(`/works?library=${books.id}`);
    expect(shell.querySelector('h1')?.textContent?.trim()).toBe('Works');
  });

  it('shows only All works when the user can see no libraries', async () => {
    const shell = await openShell('/works', (request) => request.flush([]));

    expect(sidebarLinks(shell).map((link) => link.textContent?.trim())).toEqual(['All works']);
  });

  it('shows only All works when the libraries cannot be loaded', async () => {
    const shell = await openShell('/works', (request) =>
      request.flush(null, { status: 500, statusText: 'Internal Server Error' }),
    );

    expect(sidebarLinks(shell).map((link) => link.textContent?.trim())).toEqual(['All works']);
    expect(shell.querySelector('h1')?.textContent?.trim()).toBe('Works');
  });

  it('marks All works selected when the URL names no library', async () => {
    const shell = await openShell('/works?page=2');

    expect(selectedLinks(shell)).toEqual(['All works']);
  });

  it('marks a library selected when the URL names it', async () => {
    const shell = await openShell(`/works?library=${comics.id}`);

    expect(selectedLinks(shell)).toEqual(['Comics']);
  });

  it('a library link leaves page and sort out of the URL', async () => {
    const shell = await openShell('/works?sort=added&page=3');

    linkNamed(shell, 'Books').click();
    await vi.waitFor(() => expect(router.url).toBe(`/works?library=${books.id}`));
    await harness.fixture.whenStable();
    expect(selectedLinks(shell)).toEqual(['Books']);

    linkNamed(shell, 'All works').click();
    await vi.waitFor(() => expect(router.url).toBe('/works'));
    await harness.fixture.whenStable();
    expect(selectedLinks(shell)).toEqual(['All works']);
  });

  it('collapses and expands from its trigger', async () => {
    const shell = await openShell('/works');
    const sidebar = shell.querySelector('hlm-sidebar');
    const trigger = buttonNamed(shell, 'Toggle sidebar');
    expect(sidebar?.getAttribute('data-state')).toBe('expanded');

    trigger.click();
    await harness.fixture.whenStable();
    expect(sidebar?.getAttribute('data-state')).toBe('collapsed');

    trigger.click();
    await harness.fixture.whenStable();
    expect(sidebar?.getAttribute('data-state')).toBe('expanded');
  });

  it("shows the user's email in the footer", async () => {
    const shell = await openShell('/works');

    expect(shell.querySelector('[data-slot="sidebar-footer"]')?.textContent).toContain(
      'ada@example.com',
    );
  });

  it('sign out ends the session and goes to /sign-in', async () => {
    const shell = await openShell('/works');

    await signOutFromTheFooterMenu(shell);
    const request = await vi.waitFor(() => httpTesting.expectOne('/api/auth/logout'));
    expect(request.request.method).toBe('POST');
    request.flush(null, { status: 204, statusText: 'No Content' });

    await vi.waitFor(() => expect(router.url).toBe('/sign-in'));
    expect(TestBed.inject(Session).user()).toBeNull();
  });

  it('stays signed in and says so when the API fails to sign out', async () => {
    const shell = await openShell('/works');

    await signOutFromTheFooterMenu(shell);
    (await vi.waitFor(() => httpTesting.expectOne('/api/auth/logout'))).flush(null, {
      status: 500,
      statusText: 'Internal Server Error',
    });

    await vi.waitFor(() =>
      expect(shell.querySelector('[role="alert"]')?.textContent?.trim()).toBe(
        'Could not sign out. Try again.',
      ),
    );
    expect(router.url).toBe('/works');
    expect(TestBed.inject(Session).user()).toEqual(ada);
  });

  it('closes the sidebar on a phone when a library is chosen', async () => {
    const shell = await openShell('/works');

    for (const link of sidebarLinks(shell)) {
      expect(link.hasAttribute('closeMobileSidebarOnClick')).toBe(true);
    }
  });
  it('an admin sees Add books', async () => {
    const shell = await openShell('/works');

    expect(groupLinks(shell, 'Manage').map((link) => link.textContent?.trim())).toEqual([
      'Add books',
    ]);
    expect(addBooks(shell)?.getAttribute('href')).toBe('/upload');
  });

  it('a member sees no Add books', async () => {
    await signInAs(grace);
    const shell = await openShell('/works');

    expect(addBooks(shell)).toBeUndefined();
    expect(shell.querySelector('nav[aria-label="Manage"]')).toBeNull();
  });

  it('a guest sees no Add books', async () => {
    await signInAs(guest);
    const shell = await openShell('/works');

    expect(addBooks(shell)).toBeUndefined();
    expect(shell.querySelector('nav[aria-label="Manage"]')).toBeNull();
  });

  it('Add books carries the library being browsed', async () => {
    const shell = await openShell(`/works?library=${comics.id}&page=2`);

    expect(addBooks(shell)?.getAttribute('href')).toBe(`/upload?library=${comics.id}`);

    addBooks(shell)?.click();
    await vi.waitFor(() => expect(router.url).toBe(`/upload?library=${comics.id}`));
  });

  it('Add books is the current page on /upload and All works is not', async () => {
    const shell = await openShell('/upload');

    expect(addBooks(shell)?.getAttribute('aria-current')).toBe('page');
    expect(selectedLinks(shell)).toEqual([]);
    expect(shell.querySelector('h1')?.textContent?.trim()).toBe('Add books');

    linkNamed(shell, 'All works').click();
    await vi.waitFor(() => expect(router.url).toBe('/works'));
    await harness.fixture.whenStable();
    expect(addBooks(shell)?.getAttribute('aria-current')).toBeNull();
    expect(selectedLinks(shell)).toEqual(['All works']);
  });

  it('signing out empties the upload list', async () => {
    const queue = TestBed.inject(UploadQueue);
    const shell = await openShell('/works');
    queue.add([new File(['epub bytes'], 'dune.epub')], books.id);
    const upload = await vi.waitFor(() => httpTesting.expectOne('/api/uploads'));

    await signOutFromTheFooterMenu(shell);
    (await vi.waitFor(() => httpTesting.expectOne('/api/auth/logout'))).flush(null, {
      status: 204,
      statusText: 'No Content',
    });

    await vi.waitFor(() => expect(router.url).toBe('/sign-in'));
    ignoreCancelled = true;
    expect(queue.rows()).toEqual([]);
    expect(upload.cancelled).toBe(true);
  });

  it('a failed sign-out keeps the upload list', async () => {
    const queue = TestBed.inject(UploadQueue);
    const shell = await openShell('/works');
    queue.add([new File(['epub bytes'], 'dune.epub')], books.id);
    const upload = await vi.waitFor(() => httpTesting.expectOne('/api/uploads'));

    await signOutFromTheFooterMenu(shell);
    (await vi.waitFor(() => httpTesting.expectOne('/api/auth/logout'))).flush(null, {
      status: 500,
      statusText: 'Internal Server Error',
    });

    await vi.waitFor(() => expect(shell.querySelector('[role="alert"]')).not.toBeNull());
    expect(queue.rows().map((row) => [row.name, row.status])).toEqual([['dune.epub', 'uploading']]);
    expect(upload.cancelled).toBe(false);
  });
});
