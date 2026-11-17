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

type AccessResponse = components['schemas']['AccessResponse'];
type CurrentUserResponse = components['schemas']['CurrentUserResponse'];
type SectionResponse = components['schemas']['SectionResponse'];

@Component({ template: '<h1>Works</h1>' })
class WorksStub {}

@Component({ template: '<h1>Work</h1>' })
class WorkStub {}

@Component({ template: '<h1>Add books</h1>' })
class UploadStub {}

@Component({ template: '<h1>Public access</h1>' })
class PublicAccessStub {}

@Component({ template: '<h1>Authors</h1>' })
class AuthorsStub {}

@Component({ template: '<h1>Person</h1>' })
class PersonStub {}

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
    role: 'contributor',
  };
  const viewer: CurrentUserResponse = {
    id: '0198a1b2-0000-7000-8000-000000000003',
    email: 'viewer@example.com',
    role: 'viewer',
  };
  const books: SectionResponse = { id: '0198a1b2-0000-7000-8000-00000000000a', name: 'Books' };
  const comics: SectionResponse = { id: '0198a1b2-0000-7000-8000-00000000000b', name: 'Comics' };
  const publicAccessOff: AccessResponse = { publicAccess: false, defaultArea: 'none' };

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
              { path: 'library', component: WorksStub },
              { path: 'works/:id', component: WorkStub },
              { path: 'upload', component: UploadStub },
              { path: 'public-access', component: PublicAccessStub },
              { path: 'authors', component: AuthorsStub },
              { path: 'people/:id', component: PersonStub },
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

  /** Loads the session with nobody signed in, the API saying a visitor may do what `access` says. */
  async function visitWith(access: AccessResponse): Promise<void> {
    const loading = TestBed.inject(Session).load();
    httpTesting.expectOne('/api/auth/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    (await vi.waitFor(() => httpTesting.expectOne('/api/access'))).flush(access);
    await loading;
  }

  /** The header's link named "Sign in", if there is one. */
  function signInLink(shell: HTMLElement): HTMLAnchorElement | undefined {
    return Array.from(shell.querySelectorAll<HTMLAnchorElement>('header a')).find(
      (link) => link.textContent?.trim() === 'Sign in',
    );
  }

  /** Opens the shell at `url`, answering its request for the sections with `answer`. */
  async function openShell(
    url: string,
    answer: (sections: TestRequest) => void = (request) => request.flush([books, comics]),
  ): Promise<HTMLElement> {
    const navigating = harness.navigateByUrl(url);
    answer(await vi.waitFor(() => httpTesting.expectOne('/api/sections')));
    await navigating;
    await harness.fixture.whenStable();
    return harness.fixture.nativeElement;
  }

  /** The links of the sidebar group labelled `group`. */
  function groupLinks(shell: HTMLElement, group: string): HTMLAnchorElement[] {
    return Array.from(shell.querySelectorAll<HTMLAnchorElement>(`nav[aria-label="${group}"] a`));
  }

  function sidebarLinks(shell: HTMLElement): HTMLAnchorElement[] {
    return groupLinks(shell, 'Library');
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

  it('lists Library, then Default, then the sections', async () => {
    const shell = await openShell('/library');

    expect(
      sidebarLinks(shell).map((link) => [link.textContent?.trim(), link.getAttribute('href')]),
    ).toEqual([
      ['Library', '/library'],
      ['Default', '/library?section=default'],
      ['Books', `/library?section=${books.id}`],
      ['Comics', `/library?section=${comics.id}`],
    ]);
    expect(shell.querySelector('h1')?.textContent?.trim()).toBe('Works');
  });

  it('shows only Library when the user can see no sections', async () => {
    const shell = await openShell('/library', (request) => request.flush([]));

    expect(sidebarLinks(shell).map((link) => link.textContent?.trim())).toEqual(['Library']);
  });

  it('shows only Library when the sections cannot be loaded', async () => {
    const shell = await openShell('/library', (request) =>
      request.flush(null, { status: 500, statusText: 'Internal Server Error' }),
    );

    expect(sidebarLinks(shell).map((link) => link.textContent?.trim())).toEqual(['Library']);
    expect(shell.querySelector('h1')?.textContent?.trim()).toBe('Works');
  });

  it('marks Library selected when the URL names no section', async () => {
    const shell = await openShell('/library?page=2');

    expect(selectedLinks(shell)).toEqual(['Library']);
  });

  it('marks a section selected when the URL names it', async () => {
    const shell = await openShell(`/library?section=${comics.id}`);

    expect(selectedLinks(shell)).toEqual(['Comics']);
  });

  it('marks Default current when the URL says section=default', async () => {
    const shell = await openShell('/library?section=default&page=2');

    expect(selectedLinks(shell)).toEqual(['Default']);
  });

  it("marks no link current on a work's page", async () => {
    const shell = await openShell('/works/0198a1b2-0000-7000-8000-000000000001');

    expect(selectedLinks(shell)).toEqual([]);
  });

  it('a section link leaves page and sort out of the URL', async () => {
    const shell = await openShell('/library?sort=added&page=3');

    linkNamed(shell, 'Books').click();
    await vi.waitFor(() => expect(router.url).toBe(`/library?section=${books.id}`));
    await harness.fixture.whenStable();
    expect(selectedLinks(shell)).toEqual(['Books']);

    linkNamed(shell, 'Library').click();
    await vi.waitFor(() => expect(router.url).toBe('/library'));
    await harness.fixture.whenStable();
    expect(selectedLinks(shell)).toEqual(['Library']);
  });

  it('collapses and expands from its trigger', async () => {
    const shell = await openShell('/library');
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
    const shell = await openShell('/library');

    expect(shell.querySelector('[data-slot="sidebar-footer"]')?.textContent).toContain(
      'ada@example.com',
    );
  });

  it('sign out ends the session and goes to /sign-in', async () => {
    const shell = await openShell('/library');

    await signOutFromTheFooterMenu(shell);
    const request = await vi.waitFor(() => httpTesting.expectOne('/api/auth/logout'));
    expect(request.request.method).toBe('POST');
    request.flush(null, { status: 204, statusText: 'No Content' });
    (await vi.waitFor(() => httpTesting.expectOne('/api/access'))).flush(publicAccessOff);

    await vi.waitFor(() => expect(router.url).toBe('/sign-in'));
    expect(TestBed.inject(Session).user()).toBeNull();
  });

  it('stays signed in and says so when the API fails to sign out', async () => {
    const shell = await openShell('/library');

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
    expect(router.url).toBe('/library');
    expect(TestBed.inject(Session).user()).toEqual(ada);
  });

  it('closes the sidebar on a phone when a section is chosen', async () => {
    const shell = await openShell('/library');

    for (const link of sidebarLinks(shell)) {
      expect(link.hasAttribute('closeMobileSidebarOnClick')).toBe(true);
    }
  });
  it('an admin sees Add books', async () => {
    const shell = await openShell('/library');

    expect(groupLinks(shell, 'Manage').map((link) => link.textContent?.trim())).toContain(
      'Add books',
    );
    expect(addBooks(shell)?.getAttribute('href')).toBe('/upload');
  });

  it('lists Public access under Manage for an admin only', async () => {
    const shell = await openShell('/library');

    expect(
      groupLinks(shell, 'Manage').map((link) => [
        link.textContent?.trim(),
        link.getAttribute('href'),
      ]),
    ).toEqual([
      ['Add books', '/upload'],
      ['Public access', '/public-access'],
    ]);

    for (const user of [grace, viewer]) {
      await signInAs(user);
      await harness.fixture.whenStable();
      expect(shell.querySelector('nav[aria-label="Manage"]')).toBeNull();
    }
  });

  it('Public access is the current page on /public-access and nothing else is', async () => {
    const shell = await openShell('/public-access');
    const current = () =>
      Array.from(shell.querySelectorAll('nav a[aria-current="page"]'), (link) =>
        link.textContent?.trim(),
      );

    expect(current()).toEqual(['Public access']);
    expect(groupLinks(shell, 'Manage')[1].hasAttribute('closeMobileSidebarOnClick')).toBe(true);
  });

  it('a contributor sees no Add books', async () => {
    await signInAs(grace);
    const shell = await openShell('/library');

    expect(addBooks(shell)).toBeUndefined();
    expect(shell.querySelector('nav[aria-label="Manage"]')).toBeNull();
  });

  it('a viewer sees no Add books', async () => {
    await signInAs(viewer);
    const shell = await openShell('/library');

    expect(addBooks(shell)).toBeUndefined();
    expect(shell.querySelector('nav[aria-label="Manage"]')).toBeNull();
  });

  it('Add books carries the default area when it is being browsed', async () => {
    const shell = await openShell('/library?section=default&sort=added');

    expect(addBooks(shell)?.getAttribute('href')).toBe('/upload?section=default');
  });

  it('Add books carries the section being browsed', async () => {
    const shell = await openShell(`/library?section=${comics.id}&page=2`);

    expect(addBooks(shell)?.getAttribute('href')).toBe(`/upload?section=${comics.id}`);

    addBooks(shell)?.click();
    await vi.waitFor(() => expect(router.url).toBe(`/upload?section=${comics.id}`));
  });

  it('Add books is the current page on /upload and Library is not', async () => {
    const shell = await openShell('/upload');

    expect(addBooks(shell)?.getAttribute('aria-current')).toBe('page');
    expect(selectedLinks(shell)).toEqual([]);
    expect(shell.querySelector('h1')?.textContent?.trim()).toBe('Add books');

    linkNamed(shell, 'Library').click();
    await vi.waitFor(() => expect(router.url).toBe('/library'));
    await harness.fixture.whenStable();
    expect(addBooks(shell)?.getAttribute('aria-current')).toBeNull();
    expect(selectedLinks(shell)).toEqual(['Library']);
  });

  it('shows Authors linking to /authors', async () => {
    const shell = await openShell('/library');

    expect(
      groupLinks(shell, 'Browse').map((link) => [
        link.textContent?.trim(),
        link.getAttribute('href'),
      ]),
    ).toEqual([['Authors', '/authors']]);
  });

  it('a viewer sees Authors', async () => {
    await signInAs(viewer);
    const shell = await openShell('/library');

    expect(groupLinks(shell, 'Browse').map((link) => link.textContent?.trim())).toEqual([
      'Authors',
    ]);
  });

  it('shows Authors when the user can see no sections', async () => {
    const shell = await openShell('/library', (request) => request.flush([]));

    expect(groupLinks(shell, 'Browse').map((link) => link.textContent?.trim())).toEqual([
      'Authors',
    ]);
  });

  it('Authors is the current page on /authors and Library is not', async () => {
    const shell = await openShell('/authors?page=2');

    expect(groupLinks(shell, 'Browse')[0].getAttribute('aria-current')).toBe('page');
    expect(selectedLinks(shell)).toEqual([]);
    expect(shell.querySelector('h1')?.textContent?.trim()).toBe('Authors');
  });

  it("marks no link current on a person's page", async () => {
    const shell = await openShell('/people/42');

    expect(selectedLinks(shell)).toEqual([]);
    expect(groupLinks(shell, 'Browse')[0].getAttribute('aria-current')).toBeNull();
  });

  it('closes the sidebar on a phone when Authors is chosen', async () => {
    const shell = await openShell('/library');

    const links = groupLinks(shell, 'Browse');
    expect(links).toHaveLength(1);
    for (const link of links) {
      expect(link.hasAttribute('closeMobileSidebarOnClick')).toBe(true);
    }
  });

  it('signing out empties the upload list', async () => {
    const queue = TestBed.inject(UploadQueue);
    const shell = await openShell('/library');
    queue.add([new File(['epub bytes'], 'dune.epub')], books.id);
    const upload = await vi.waitFor(() => httpTesting.expectOne('/api/uploads'));

    await signOutFromTheFooterMenu(shell);
    (await vi.waitFor(() => httpTesting.expectOne('/api/auth/logout'))).flush(null, {
      status: 204,
      statusText: 'No Content',
    });
    (await vi.waitFor(() => httpTesting.expectOne('/api/access'))).flush(publicAccessOff);

    await vi.waitFor(() => expect(router.url).toBe('/sign-in'));
    ignoreCancelled = true;
    expect(queue.rows()).toEqual([]);
    expect(upload.cancelled).toBe(true);
  });

  it('shows a visitor a sign-in link and no user menu', async () => {
    await visitWith({ publicAccess: true, defaultArea: 'browse' });
    const shell = await openShell('/library');

    expect(signInLink(shell)?.getAttribute('href')).toBe('/sign-in');
    expect(shell.querySelectorAll('[data-slot="sidebar-footer"] button')).toHaveLength(0);
  });

  it('shows a signed-in user no sign-in link', async () => {
    const shell = await openShell('/library');

    expect(signInLink(shell)).toBeUndefined();
  });

  it('shows a visitor Default when their Default area level is not none', async () => {
    await visitWith({ publicAccess: true, defaultArea: 'read' });
    const shell = await openShell('/library', (request) => request.flush([comics]));

    expect(sidebarLinks(shell).map((link) => link.textContent?.trim())).toEqual([
      'Library',
      'Default',
      'Comics',
    ]);
  });

  it('hides Default from a visitor whose Default area level is none', async () => {
    await visitWith({ publicAccess: true, defaultArea: 'none' });
    const shell = await openShell('/library', (request) => request.flush([comics]));

    expect(sidebarLinks(shell).map((link) => link.textContent?.trim())).toEqual([
      'Library',
      'Comics',
    ]);
  });

  it('shows a visitor no Manage group', async () => {
    await visitWith({ publicAccess: true, defaultArea: 'download' });
    const shell = await openShell('/library');

    expect(shell.querySelector('nav[aria-label="Manage"]')).toBeNull();
    expect(addBooks(shell)).toBeUndefined();
  });

  it('a failed sign-out keeps the upload list', async () => {
    const queue = TestBed.inject(UploadQueue);
    const shell = await openShell('/library');
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
