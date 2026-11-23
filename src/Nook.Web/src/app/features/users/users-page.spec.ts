import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { components } from '../../api/schema';
import { Session } from '../../core/session';
import { buttonLabelled } from '../../testing/button-labelled';
import { text } from '../../testing/element-text';
import { UsersPage } from './users-page';

type CurrentUserResponse = components['schemas']['CurrentUserResponse'];
type UserSummaryResponse = components['schemas']['UserSummaryResponse'];

describe('UsersPage', () => {
  const adaId = '0198a1b2-0000-7000-8000-000000000001';
  const graceId = '0198a1b2-0000-7000-8000-000000000002';
  const linusId = '0198a1b2-0000-7000-8000-000000000003';

  const signedInAda: CurrentUserResponse = {
    id: adaId,
    email: 'ada@example.com',
    role: 'admin',
    displayName: 'Ada',
    canUpload: true,
  };

  const ada: UserSummaryResponse = {
    id: adaId,
    name: 'Ada',
    email: 'ada@example.com',
    role: 'Admin',
    joinedAt: '2026-01-05T12:00:00Z',
    hasPassword: true,
    hasSingleSignOn: false,
    hasAuditEntries: true,
  };
  const grace: UserSummaryResponse = {
    id: graceId,
    name: 'Grace',
    email: 'grace@example.com',
    role: 'Contributor',
    joinedAt: '2026-02-10T12:00:00Z',
    hasPassword: false,
    hasSingleSignOn: true,
    hasAuditEntries: false,
  };
  const linus: UserSummaryResponse = {
    id: linusId,
    name: 'Linus',
    email: 'linus@example.com',
    role: 'Viewer',
    joinedAt: '2026-03-15T12:00:00Z',
    hasPassword: true,
    hasSingleSignOn: true,
    hasAuditEntries: true,
  };

  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'users', component: UsersPage }]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    const loading = TestBed.inject(Session).load();
    httpTesting.expectOne('/api/auth/me').flush(signedInAda);
    await loading;
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => httpTesting.verify());

  function usersRequest(): Promise<TestRequest> {
    return vi.waitFor(() => httpTesting.expectOne((request) => request.url === '/api/users'));
  }

  /** Opens the page and answers its request with `users`. */
  async function openUsers(
    users: UserSummaryResponse[] = [ada, grace, linus],
    url = '/users',
    total = users.length,
  ): Promise<HTMLElement> {
    const navigating = harness.navigateByUrl(url);
    (await usersRequest()).flush({ items: users, total, offset: 0, limit: 50 });
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await vi.waitFor(() => expect(page.querySelector('table, [role="alert"]')).not.toBeNull());
    return page;
  }

  function rowOf(page: HTMLElement, name: string): HTMLTableRowElement {
    const row = Array.from(page.querySelectorAll<HTMLTableRowElement>('tbody tr')).find((r) =>
      text(r.querySelector('td')).startsWith(name),
    );
    if (!row) {
      throw new Error(`No row for ${name}`);
    }
    return row;
  }

  function cellTexts(row: HTMLTableRowElement): string[] {
    return Array.from(row.querySelectorAll('td'), (cell) => text(cell));
  }

  /** Answers the page's next request for users, and returns what it asked for. */
  async function answerNext(total = 3): Promise<Record<string, string | null>> {
    const request = await usersRequest();
    const asked = Object.fromEntries(
      request.request.params.keys().map((key) => [key, request.request.params.get(key)]),
    );
    request.flush({ items: [ada, grace, linus], total, offset: 0, limit: 50 });
    return asked;
  }

  function header(page: HTMLElement, name: string): HTMLTableCellElement {
    const found = Array.from(page.querySelectorAll<HTMLTableCellElement>('th')).find(
      (th) => text(th) === name,
    );
    if (!found) {
      throw new Error(`No header ${name}`);
    }
    return found;
  }

  function click(page: HTMLElement, name: string): void {
    const target = header(page, name).querySelector('button');
    if (!target) {
      throw new Error(`No button in ${name}`);
    }
    target.click();
  }

  it("shows each user's name, email, role, joined date and how they sign in", async () => {
    const page = await openUsers();

    expect(Array.from(page.querySelectorAll('th'), (cell) => text(cell))).toEqual([
      'Name',
      'Email',
      'Role',
      'Joined',
      'Sign-in',
      '',
    ]);
    expect(page.querySelectorAll('tbody tr')).toHaveLength(3);
    expect(cellTexts(rowOf(page, 'Grace')).slice(0, 4)).toEqual([
      'Grace',
      'grace@example.com',
      'Contributor',
      'Feb 10, 2026',
    ]);
    const signIn = (name: string) => text(rowOf(page, name).querySelectorAll('td')[4]);
    expect(signIn('Ada')).toBe('Password');
    expect(signIn('Grace')).toBe('Single sign-on');
    expect(signIn('Linus')).toBe('Password and single sign-on');
  });

  it("marks the signed-in admin's own row", async () => {
    const page = await openUsers();

    const marked = Array.from(page.querySelectorAll('tbody tr'))
      .filter((row) => text(row).includes('You'))
      .map((row) => text(row.querySelector('td span')));
    expect(marked).toEqual(['Ada']);
  });

  it('links to the audit log only for someone it names', async () => {
    const page = await openUsers();

    const hrefOf = (name: string) =>
      rowOf(page, name).querySelector<HTMLAnchorElement>('a')?.getAttribute('href');
    expect(hrefOf('Ada')).toBe(`/admin/audit-log?person=${adaId}`);
    expect(hrefOf('Linus')).toBe(`/admin/audit-log?person=${linusId}`);
    expect(hrefOf('Grace')).toBeUndefined();
    expect(rowOf(page, 'Ada').querySelector('a')?.getAttribute('aria-label')).toBe(
      'Audit log for Ada',
    );
  });

  it('offers to try again when the list fails to load', async () => {
    const navigating = harness.navigateByUrl('/users');
    (await usersRequest()).flush(null, { status: 500, statusText: 'Server Error' });
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await vi.waitFor(() => expect(page.querySelector('[role="alert"]')).not.toBeNull());

    const retry = Array.from(page.querySelectorAll('button')).find((b) =>
      /try again|retry/i.test(text(b)),
    );
    expect(retry).toBeDefined();
    retry?.click();

    (await usersRequest()).flush({ items: [ada], total: 1, offset: 0, limit: 50 });
    await vi.waitFor(() => expect(page.querySelector('table')).not.toBeNull());
  });

  it('asks for the first page by name when the URL names nothing', async () => {
    const navigating = harness.navigateByUrl('/users');
    const asked = await answerNext();
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await vi.waitFor(() => expect(page.querySelector('table')).not.toBeNull());

    expect(asked).toEqual({ Offset: '0', Limit: '50', Sort: 'name', Direction: 'asc' });
    expect(header(page, 'Name').getAttribute('aria-sort')).toBe('ascending');
  });

  it('sorts by the header clicked and reverses on a second click', async () => {
    const page = await openUsers();

    click(page, 'Joined');
    expect(await answerNext()).toMatchObject({ Sort: 'joined', Direction: 'asc' });
    expect(TestBed.inject(Router).url).toBe('/users?sort=joined');

    await vi.waitFor(() =>
      expect(header(page, 'Joined').getAttribute('aria-sort')).toBe('ascending'),
    );
    click(page, 'Joined');
    expect(await answerNext()).toMatchObject({ Sort: 'joined', Direction: 'desc' });
    expect(TestBed.inject(Router).url).toBe('/users?sort=joined&dir=desc');
    await vi.waitFor(() =>
      expect(header(page, 'Joined').getAttribute('aria-sort')).toBe('descending'),
    );
  });

  it('takes the default sort out of the URL', async () => {
    const page = await openUsers(undefined, '/users?sort=email&dir=desc');

    click(page, 'Name');
    await answerNext();

    expect(TestBed.inject(Router).url).toBe('/users');
  });

  it('opens on the sort and page the URL names', async () => {
    const navigating = harness.navigateByUrl('/users?sort=role&dir=desc&page=2');
    const asked = await answerNext();
    await navigating;

    expect(asked).toMatchObject({ Offset: '50', Sort: 'role', Direction: 'desc' });
  });

  it('returns to the first page when the sort changes', async () => {
    const page = await openUsers(undefined, '/users?page=2');

    click(page, 'Email');
    expect(await answerNext()).toMatchObject({ Offset: '0', Sort: 'email' });
    expect(TestBed.inject(Router).url).toBe('/users?sort=email');
  });

  it('does not offer to sort by how someone signs in', async () => {
    const page = await openUsers();

    expect(header(page, 'Sign-in').querySelector('button')).toBeNull();
    header(page, 'Sign-in').click();

    expect(TestBed.inject(Router).url).toBe('/users');
    httpTesting.expectNone((request) => request.url === '/api/users');
  });

  it('treats a sort, direction or page it does not know as the default', async () => {
    const navigating = harness.navigateByUrl('/users?sort=shoe&dir=sideways&page=0');
    const asked = await answerNext();
    await navigating;

    expect(asked).toEqual({ Offset: '0', Limit: '50', Sort: 'name', Direction: 'asc' });
  });

  it('pages through the list', async () => {
    const page = await openUsers(undefined, '/users', 120);

    expect(text(page.querySelector('nav'))).toContain('120 users');
    expect(text(page.querySelector('nav'))).toContain('Page 1 of 3');
    expect(buttonLabelled(page, 'Previous').disabled).toBe(true);

    buttonLabelled(page, 'Next').click();
    expect(await answerNext(120)).toMatchObject({ Offset: '50' });
    expect(TestBed.inject(Router).url).toBe('/users?page=2');

    await TestBed.inject(Router).navigateByUrl('/users?page=3');
    await answerNext(120);
    await vi.waitFor(() => expect(buttonLabelled(page, 'Next').disabled).toBe(true));
  });

  it('keeps the sort when paging', async () => {
    const page = await openUsers(undefined, '/users?sort=joined&dir=desc', 120);

    buttonLabelled(page, 'Next').click();
    await answerNext(120);

    expect(TestBed.inject(Router).url).toBe('/users?sort=joined&dir=desc&page=2');
  });
});
