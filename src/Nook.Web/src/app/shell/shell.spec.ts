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
import { PageName } from '../core/page-name';
import { AppTheme } from '../core/app-theme';
import { Session } from '../core/session';
import { UploadActivity } from '../features/activity/upload-activity';
import { AddBooks } from '../features/upload/add-books';
import { UploadQueue } from '../features/upload/upload-queue';
import { FakeActivityHub, provideFakeActivityHub } from '../testing/fake-activity-hub';
import { signInAs } from '../testing/sign-in-as';
import { typeInto } from '../testing/type-into';
import { flushUploadActivity } from '../testing/upload-fixtures';
import { Shell } from './shell';

type AccessResponse = components['schemas']['AccessResponse'];
type CurrentUserResponse = components['schemas']['CurrentUserResponse'];
type SectionResponse = components['schemas']['SectionResponse'];
type UploadActivityResponse = components['schemas']['UploadActivityResponse'];

@Component({ template: '<h1>Works</h1>' })
class WorksStub {}

@Component({ template: '<h1>Work</h1>' })
class WorkStub {}

@Component({ template: '<h1>Public access</h1>' })
class PublicAccessStub {}

@Component({ template: '<h1>Jobs</h1>' })
class JobsStub {}

@Component({ template: '<h1>Audit log</h1>' })
class AuditLogStub {}

@Component({ template: '<h1>Users</h1>' })
class UsersStub {}

@Component({ template: '<h1>Authors</h1>' })
class AuthorsStub {}

@Component({ template: '<h1>Person</h1>' })
class PersonStub {}

@Component({ template: '<h1>Sign in</h1>' })
class SignInStub {}

describe('Shell', () => {
  const anUpload: UploadActivityResponse = {
    jobId: '0198a1b2-0000-7000-8000-0000000000f1',
    fileName: 'dune.epub',
    sectionId: null,
    sectionName: null,
    submittedAt: '2026-11-20T10:00:00Z',
    status: 'queued',
    workId: null,
    failure: null,
  };
  const ada: CurrentUserResponse = {
    id: '0198a1b2-0000-7000-8000-000000000001',
    email: 'ada@example.com',
    role: 'admin',
    displayName: 'Admin User',
    canUpload: true,
  };
  const grace: CurrentUserResponse = {
    id: '0198a1b2-0000-7000-8000-000000000002',
    email: 'grace@example.com',
    role: 'contributor',
    displayName: 'Contributor User',
    canUpload: true,
  };
  const viewer: CurrentUserResponse = {
    id: '0198a1b2-0000-7000-8000-000000000003',
    email: 'viewer@example.com',
    role: 'viewer',
    displayName: 'Viewer User',
    canUpload: false,
  };
  const books: SectionResponse = { id: '0198a1b2-0000-7000-8000-00000000000a', name: 'Books' };
  const comics: SectionResponse = { id: '0198a1b2-0000-7000-8000-00000000000b', name: 'Comics' };
  const publicAccessOff: AccessResponse = { publicAccess: false, defaultArea: 'none' };

  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let router: Router;
  let ignoreCancelled: boolean;
  const openAddBooks = vi.fn<(files: readonly File[], sectionParam: string | null) => void>();

  beforeEach(async () => {
    ignoreCancelled = false;
    openAddBooks.mockReset();
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
              { path: 'admin/public-access', component: PublicAccessStub },
              { path: 'admin/jobs', component: JobsStub },
              { path: 'admin/audit-log', component: AuditLogStub },
              { path: 'admin/users', component: UsersStub },
              { path: 'authors', component: AuthorsStub },
              { path: 'people/:id', component: PersonStub },
            ],
          },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideFakeActivityHub(),
        { provide: AddBooks, useValue: { open: openAddBooks } },
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);

    await signInAs(ada);

    harness = await RouterTestingHarness.create();
  });

  afterEach(() => {
    // Whoever may upload has the shell list their uploads, which no spec here is about unless it says so.
    flushUploadActivity(httpTesting);
    httpTesting.verify({ ignoreCancelled });
    // The sidebar remembers whether it is open in a cookie, which would carry over to the next test.
    document.cookie = 'sidebar_state=; path=/; max-age=0';
  });

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

  /** The footer's "Upload" button, which opens the "Add books" dialog, when the sidebar has one. */
  function uploadButton(shell: HTMLElement): HTMLButtonElement | undefined {
    return Array.from(
      shell.querySelectorAll<HTMLButtonElement>('[data-slot="sidebar-footer"] button'),
    ).find((link) => link.textContent?.trim() === 'Upload');
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

  /** Opens the user's menu in the sidebar's footer and presses its item called `name`. */
  async function chooseFromTheFooterMenu(shell: HTMLElement, name: string): Promise<void> {
    shell
      .querySelector<HTMLElement>('[data-slot="sidebar-footer"] button:has([data-user-email])')!
      .click();
    await harness.fixture.whenStable();
    const item = await vi.waitFor(() => {
      const found = Array.from(document.querySelectorAll<HTMLElement>('[role="menuitem"]')).find(
        (candidate) => candidate.textContent?.trim() === name,
      );
      if (!found) {
        throw new Error(`The menu has no "${name}" item.`);
      }
      return found;
    });
    item.click();
  }

  function signOutFromTheFooterMenu(shell: HTMLElement): Promise<void> {
    return chooseFromTheFooterMenu(shell, 'Sign out');
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

  /** What each step of the header's trail says, and where the ones that are links lead. */
  function trail(shell: HTMLElement): (string | null)[][] {
    return Array.from(
      shell.querySelectorAll('nav[aria-label="Breadcrumb"] [data-slot="breadcrumb-item"]'),
      (step) => [
        step.textContent?.trim() ?? '',
        step.querySelector('a')?.getAttribute('href') ?? null,
      ],
    );
  }

  it('the header names the page and the tab is titled after it', async () => {
    const shell = await openShell('/authors');

    expect(trail(shell)).toEqual([['Authors', null]]);
    expect(document.title).toBe('Authors · Nook');
  });

  it('names the Jobs page and the Audit log page under Admin in the header', async () => {
    const shell = await openShell('/admin/jobs?show=all');
    expect(trail(shell)).toEqual([
      ['Admin', null],
      ['Jobs', null],
    ]);
    expect(document.title).toBe('Jobs · Nook');

    await router.navigateByUrl('/admin/audit-log');
    await harness.fixture.whenStable();
    expect(trail(shell)).toEqual([
      ['Admin', null],
      ['Audit log', null],
    ]);
    expect(document.title).toBe('Audit log · Nook');
  });

  it('names the Users page under Admin in the header', async () => {
    const shell = await openShell('/admin/users?sort=joined');

    expect(trail(shell)).toEqual([
      ['Admin', null],
      ['Users', null],
    ]);
    expect(document.title).toBe('Users · Nook');
  });

  it('the header shows the section being browsed under Library', async () => {
    const shell = await openShell(`/library?section=${comics.id}`);

    expect(trail(shell)).toEqual([
      ['Library', '/library'],
      ['Comics', null],
    ]);
    expect(document.title).toBe('Comics · Nook');
  });

  it('the header shows a search under Library', async () => {
    const shell = await openShell('/library?q=dune');

    expect(trail(shell)).toEqual([
      ['Library', '/library'],
      ['Search', null],
    ]);
  });

  it("the header shows a work's title under Library once the page has said it", async () => {
    const id = '0198a1b2-0000-7000-8000-000000000001';
    const shell = await openShell(`/works/${id}`);
    expect(trail(shell)).toEqual([
      ['Library', '/library'],
      ['', null],
    ]);
    expect(document.title).toBe('Nook');

    TestBed.inject(PageName).set(id, 'Dune');
    await harness.fixture.whenStable();

    expect(trail(shell)).toEqual([
      ['Library', '/library'],
      ['Dune', null],
    ]);
    expect(document.title).toBe('Dune · Nook');
  });

  it('the header finds the name of a page whose id the URL encodes or follows with parameters', async () => {
    TestBed.inject(PageName).set('no such', 'Work not found');
    const shell = await openShell('/works/no%20such;from=mail');

    expect(trail(shell)).toEqual([
      ['Library', '/library'],
      ['Work not found', null],
    ]);
  });

  it('the header does not show a name another page left behind', async () => {
    TestBed.inject(PageName).set('0198a1b2-0000-7000-8000-000000000009', 'Dune');
    const shell = await openShell('/people/0198a1b2-0000-7000-8000-000000000001');

    expect(trail(shell)).toEqual([
      ['Authors', '/authors'],
      ['', null],
    ]);
  });

  it('the app name in the sidebar leads to the library', async () => {
    const shell = await openShell('/authors');

    const home = shell.querySelector<HTMLAnchorElement>('[data-slot="sidebar-header"] a');
    expect(home?.textContent?.trim()).toBe('Nook');
    expect(home?.getAttribute('href')).toBe('/library');
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

  it('the footer menu opens a dialog that chooses the theme, and stays open', async () => {
    const shell = await openShell('/library');
    const theme = TestBed.inject(AppTheme);

    await chooseFromTheFooterMenu(shell, 'Theme');
    await harness.fixture.whenStable();

    const themes = (): HTMLElement[] =>
      Array.from(document.querySelectorAll<HTMLElement>('app-theme-dialog button'));
    await vi.waitFor(() =>
      expect(themes().map((item) => item.textContent?.trim())).toEqual(['Auto', 'Light', 'Dark']),
    );
    const chosen = (): (string | null)[] => themes().map((item) => item.getAttribute('data-state'));
    expect(chosen()).toEqual(['on', 'off', 'off']);

    try {
      themes()[2].click();
      await harness.fixture.whenStable();

      expect(theme.choice()).toBe('dark');
      expect(document.documentElement.classList.contains('dark')).toBe(true);
      expect(chosen()).toEqual(['off', 'off', 'on']);

      // Pressing the theme already chosen leaves it chosen.
      themes()[2].click();
      await harness.fixture.whenStable();
      expect(theme.choice()).toBe('dark');
    } finally {
      theme.choose('auto');
      localStorage.removeItem('nook.theme');
    }
  });

  it('sign out ends the session and goes to /sign-in', async () => {
    TestBed.inject(PageName).set('opened', 'Dune');
    const shell = await openShell('/library');

    await signOutFromTheFooterMenu(shell);
    const request = await vi.waitFor(() => httpTesting.expectOne('/api/auth/logout'));
    expect(request.request.method).toBe('POST');
    request.flush(null, { status: 204, statusText: 'No Content' });
    (await vi.waitFor(() => httpTesting.expectOne('/api/access'))).flush(publicAccessOff);

    await vi.waitFor(() => expect(router.url).toBe('/sign-in'));
    expect(TestBed.inject(Session).user()).toBeNull();
    expect(TestBed.inject(PageName).of('opened')).toBeNull();
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
  it('shows the Admin group to an admin and to no one else', async () => {
    const shell = await openShell('/library');

    expect(
      groupLinks(shell, 'Admin').map((link) => [
        link.textContent?.trim(),
        link.getAttribute('href'),
      ]),
    ).toEqual([
      ['Users', '/admin/users'],
      ['Public access', '/admin/public-access'],
      ['Jobs', '/admin/jobs'],
      ['Audit log', '/admin/audit-log'],
    ]);

    for (const user of [grace, viewer]) {
      await signInAs(user);
      await harness.fixture.whenStable();
      expect(shell.querySelector('nav[aria-label="Admin"]')).toBeNull();
    }
  });

  it('Public access is the current page on /admin/public-access and nothing else is', async () => {
    const shell = await openShell('/admin/public-access');
    const current = () =>
      Array.from(shell.querySelectorAll('nav a[aria-current="page"]'), (link) =>
        link.textContent?.trim(),
      );

    expect(current()).toEqual(['Public access']);
    expect(groupLinks(shell, 'Admin')[1].hasAttribute('closeMobileSidebarOnClick')).toBe(true);
  });

  it('marks Jobs or Audit log current on its own page and nothing else', async () => {
    const shell = await openShell('/admin/jobs?page=2');
    const current = () =>
      Array.from(shell.querySelectorAll('nav a[aria-current="page"]'), (link) =>
        link.textContent?.trim(),
      );
    expect(current()).toEqual(['Jobs']);

    await router.navigateByUrl('/admin/audit-log?person=x');
    await harness.fixture.whenStable();
    expect(current()).toEqual(['Audit log']);
  });

  it('marks Users current on its own page and nothing else', async () => {
    const shell = await openShell('/admin/users?page=2');

    expect(
      Array.from(shell.querySelectorAll('nav a[aria-current="page"]'), (link) =>
        link.textContent?.trim(),
      ),
    ).toEqual(['Users']);
  });

  it("opens the Add books dialog from the sidebar's footer for a contributor", async () => {
    await signInAs(grace);
    const shell = await openShell('/library');

    const button = uploadButton(shell);
    // It stands above the user's menu, the footer's only other button.
    expect(
      Array.from(shell.querySelectorAll('[data-slot="sidebar-footer"] button')).indexOf(button!),
    ).toBe(0);
    expect(Array.from(groupLinks(shell, 'Browse'), (link) => link.textContent?.trim())).toEqual([
      'Authors',
    ]);
    expect(shell.querySelector('nav[aria-label="Admin"]')).toBeNull();

    button?.click();
    expect(openAddBooks).toHaveBeenCalledExactlyOnceWith([], null);
    expect(router.url).toBe('/library');
  });

  it('opens Add books for an admin with the section being browsed preselected', async () => {
    const shell = await openShell(`/library?section=${comics.id}&page=2`);

    uploadButton(shell)?.click();

    expect(openAddBooks).toHaveBeenLastCalledWith([], comics.id);
  });

  it('opens Add books with the default area when it is being browsed', async () => {
    const shell = await openShell('/library?section=default&sort=added');

    uploadButton(shell)?.click();

    expect(openAddBooks).toHaveBeenLastCalledWith([], 'default');
  });

  it('shows no Upload button to a viewer', async () => {
    await signInAs(viewer);
    const shell = await openShell('/library');

    expect(uploadButton(shell)).toBeUndefined();
    expect(shell.querySelector('nav[aria-label="Admin"]')).toBeNull();
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

  it('lists the uploads of a user who may upload, live from the hub', async () => {
    const hub = TestBed.inject(FakeActivityHub);
    const activity = TestBed.inject(UploadActivity);
    await openShell('/library');

    (await vi.waitFor(() => httpTesting.expectOne('/api/activity/uploads'))).flush([]);
    expect(hub.open).toBe(true);
    hub.uploadChanged.next(anUpload);
    expect(activity.rows()).toEqual([anUpload]);
  });

  it('follows no uploads for a user who may not upload', async () => {
    await visitWith({ publicAccess: true, defaultArea: 'browse' });
    await openShell('/library');

    httpTesting.expectNone('/api/activity/uploads');
    expect(TestBed.inject(FakeActivityHub).open).toBe(false);
  });

  it('opens the hub for a signed-in user who may not upload, without following uploads', async () => {
    await signInAs(viewer);
    await openShell('/library');

    httpTesting.expectNone('/api/activity/uploads');
    expect(TestBed.inject(FakeActivityHub).open).toBe(true);
  });

  it('shows Notifications to a user who may upload and not to a viewer', async () => {
    const shell = await openShell('/library');
    expect(shell.querySelector('header [data-slot="popover-trigger"]')?.textContent).toContain(
      'Notifications',
    );

    await signInAs(viewer);
    await vi.waitFor(() => expect(shell.querySelector('[data-slot="popover-trigger"]')).toBeNull());
  });

  it('clears the waiting files and the Notifications list on sign-out', async () => {
    const hub = TestBed.inject(FakeActivityHub);
    const activity = TestBed.inject(UploadActivity);
    const queue = TestBed.inject(UploadQueue);
    const shell = await openShell('/library');
    (await vi.waitFor(() => httpTesting.expectOne('/api/activity/uploads'))).flush([anUpload]);
    await vi.waitFor(() => expect(activity.rows()).toEqual([anUpload]));
    // Only one file is sent at a time, so the second waits its turn.
    queue.add([new File(['a'], 'dune.epub'), new File(['b'], 'emma.epub')], books.id);
    await vi.waitFor(() => httpTesting.expectOne('/api/uploads'));
    expect(queue.rows().map((row) => row.status)).toEqual(['uploading', 'waiting']);

    await signOutFromTheFooterMenu(shell);
    (await vi.waitFor(() => httpTesting.expectOne('/api/auth/logout'))).flush(null, {
      status: 204,
      statusText: 'No Content',
    });
    (await vi.waitFor(() => httpTesting.expectOne('/api/access'))).flush(publicAccessOff);

    await vi.waitFor(() => expect(router.url).toBe('/sign-in'));
    ignoreCancelled = true;
    expect(queue.rows()).toEqual([]);
    expect(hub.open).toBe(false);
    expect(activity.rows()).toEqual([]);
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

  it('shows a visitor no Admin group', async () => {
    await visitWith({ publicAccess: true, defaultArea: 'download' });
    const shell = await openShell('/library');

    expect(shell.querySelector('nav[aria-label="Admin"]')).toBeNull();
    expect(uploadButton(shell)).toBeUndefined();
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

  it('the header holds no search field', async () => {
    const shell = await openShell('/library');
    expect(shell.querySelector('header')?.querySelector('input')).toBeNull();
  });

  it('the header holds no search field for a visitor with public access', async () => {
    await visitWith({ publicAccess: true, defaultArea: 'browse' });
    const shell = await openShell('/library');
    expect(shell.querySelector('header')?.querySelector('input')).toBeNull();
  });

  describe('search', () => {
    afterEach(() => vi.useRealTimers());

    function searchButtons(shell: HTMLElement): HTMLButtonElement[] {
      return Array.from(
        shell.querySelectorAll<HTMLButtonElement>('header button[aria-label="Search"]'),
      );
    }

    const palette = (): HTMLElement | null => document.querySelector('[role="dialog"]');
    const paletteInput = (): HTMLInputElement | null =>
      document.querySelector<HTMLInputElement>('[role="dialog"] input');

    function pressShortcut(init: KeyboardEventInit): KeyboardEvent {
      const event = new KeyboardEvent('keydown', {
        key: 'k',
        bubbles: true,
        cancelable: true,
        ...init,
      });
      document.dispatchEvent(event);
      return event;
    }

    async function settle(): Promise<void> {
      await harness.fixture.whenStable();
    }

    function pressEscape(): void {
      paletteInput()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    }

    async function expectClosed(): Promise<void> {
      await vi.waitFor(() => expect(palette()).toBeNull());
    }

    it('the header offers Search as a field-like button and as an icon button', async () => {
      const shell = await openShell('/library');

      const [field, icon] = searchButtons(shell);
      expect(searchButtons(shell)).toHaveLength(2);
      expect(field.textContent).toContain('Search…');
      expect(field.textContent).toContain('⌘K');
      expect(field.classList.contains('hidden')).toBe(true);
      expect(field.classList.contains('md:flex')).toBe(true);
      expect(icon.textContent?.trim()).toBe('');
      expect(icon.classList.contains('md:hidden')).toBe(true);
    });

    it('shows the Search buttons to a visitor', async () => {
      await visitWith({ publicAccess: true, defaultArea: 'browse' });
      const shell = await openShell('/library');

      expect(searchButtons(shell)).toHaveLength(2);
    });

    it('each Search button opens the palette with its hint', async () => {
      const shell = await openShell('/library');

      for (const index of [0, 1]) {
        searchButtons(shell)[index].click();
        await vi.waitFor(() => expect(paletteInput()).not.toBeNull());
        expect(paletteInput()!.placeholder).toBe('Search…');
        expect(palette()!.textContent).toContain('Type to search the library');

        pressEscape();
        await expectClosed();
      }
    });

    it.each([{ metaKey: true }, { ctrlKey: true }])(
      '%o opens the palette, and again closes it',
      async (init) => {
        await openShell('/library');

        const opening = pressShortcut(init);
        expect(opening.defaultPrevented).toBe(true);
        await vi.waitFor(() => expect(palette()).not.toBeNull());

        pressShortcut(init);
        await expectClosed();
      },
    );

    it('a plain k opens nothing', async () => {
      await openShell('/library');

      pressShortcut({});
      await settle();

      expect(palette()).toBeNull();
    });

    it('the palette opens from a page other than the library', async () => {
      await openShell('/works/0198a1b2-0000-7000-8000-000000000001');

      pressShortcut({ metaKey: true });

      await vi.waitFor(() => expect(palette()).not.toBeNull());
    });

    it('opens empty after being closed with a term typed', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
      await openShell('/library');

      pressShortcut({ metaKey: true });
      await vi.waitFor(() => expect(paletteInput()).not.toBeNull());
      typeInto(paletteInput()!, 'du');
      await settle();
      expect(paletteInput()!.value).toBe('du');

      pressShortcut({ metaKey: true });
      await expectClosed();
      pressShortcut({ metaKey: true });
      await vi.waitFor(() => expect(paletteInput()).not.toBeNull());

      await vi.waitFor(() => expect(paletteInput()!.value).toBe(''));
      expect(palette()!.textContent).toContain('Type to search the library');
    });
  });
});
