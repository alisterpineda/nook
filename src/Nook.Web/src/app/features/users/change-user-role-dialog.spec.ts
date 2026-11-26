import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { components } from '../../api/schema';
import { Session } from '../../core/session';
import { buttonLabelled } from '../../testing/button-labelled';
import { text } from '../../testing/element-text';
import { fieldLabelled } from '../../testing/field-labelled';
import { menuItemLabelled, menuItems } from '../../testing/menu-item-labelled';
import { openUsersPage, usersRequest } from '../../testing/users-page-requests';
import { UsersPage } from './users-page';

type CurrentUserResponse = components['schemas']['CurrentUserResponse'];
type UserSummaryResponse = components['schemas']['UserSummaryResponse'];

describe('ChangeUserRoleDialog', () => {
  const adaId = '0198a1b2-0000-7000-8000-000000000001';
  const graceId = '0198a1b2-0000-7000-8000-000000000002';
  const linusId = '0198a1b2-0000-7000-8000-000000000003';
  const noorId = '0198a1b2-0000-7000-8000-000000000004';

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
    builtInRole: 'admin',
    roleFromSingleSignOn: false,
    joinedAt: '2026-01-05T12:00:00Z',
    hasPassword: true,
    hasSingleSignOn: false,
    hasAuditEntries: true,
  };
  const grace: UserSummaryResponse = {
    ...ada,
    id: graceId,
    name: 'Grace',
    email: 'grace@example.com',
    role: 'Viewer',
    builtInRole: 'viewer',
  };
  const linus: UserSummaryResponse = {
    ...ada,
    id: linusId,
    name: 'Linus',
    email: 'linus@example.com',
    role: 'Contributor',
    builtInRole: 'contributor',
    hasSingleSignOn: true,
    roleFromSingleSignOn: true,
  };
  const noor: UserSummaryResponse = {
    ...ada,
    id: noorId,
    name: 'Noor',
    email: 'noor@example.com',
    role: 'Librarian',
    builtInRole: null,
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

  afterEach(() => {
    document
      .querySelectorAll('.cdk-overlay-container')
      .forEach((overlay) => overlay.replaceChildren());
    httpTesting.verify();
  });

  async function settle(): Promise<void> {
    await TestBed.inject(ApplicationRef).whenStable();
    TestBed.tick();
  }

  async function openPage(): Promise<HTMLElement> {
    const page = await openUsersPage(harness, httpTesting, { users: [ada, grace, linus, noor] });
    await settle();
    return page;
  }

  function actionsFor(page: HTMLElement, name: string): HTMLElement {
    return page.querySelector<HTMLElement>(`button[aria-label="Actions for ${name}"]`)!;
  }

  /** Opens the page, chooses Change role from the named user's menu and returns the dialog. */
  async function openDialog(name: string): Promise<HTMLElement> {
    const page = await openPage();
    actionsFor(page, name).click();
    (await vi.waitFor(() => menuItemLabelled('Change role'))).click();
    return vi.waitFor(() => {
      const found = document.querySelector<HTMLElement>('app-change-user-role-dialog');
      expect(found).not.toBeNull();
      return found!;
    });
  }

  function select(dialog: HTMLElement): HTMLSelectElement {
    return fieldLabelled(dialog, 'Role', HTMLSelectElement);
  }

  function choose(dialog: HTMLElement, role: string): void {
    const field = select(dialog);
    field.value = role;
    field.dispatchEvent(new Event('change'));
  }

  function roleRequest(id = graceId) {
    return vi.waitFor(() => httpTesting.expectOne(`/api/users/${id}/role`));
  }

  it('changes the role and reloads the list', async () => {
    const dialog = await openDialog('Grace');

    expect(select(dialog).selectedOptions[0].text.trim()).toBe('Viewer');
    choose(dialog, 'admin');
    buttonLabelled(dialog, 'Save').click();

    const request = await roleRequest();
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ role: 'admin' });
    request.flush(null, { status: 204, statusText: 'No Content' });

    const reload = await usersRequest(httpTesting);
    expect(reload.request.method).toBe('GET');
    reload.flush({ items: [ada, linus, noor], total: 3, offset: 0, limit: 50 });
    await settle();
    expect(document.querySelector('app-change-user-role-dialog')).toBeNull();
  });

  it('shows why the API refused', async () => {
    const dialog = await openDialog('Grace');

    choose(dialog, 'contributor');
    buttonLabelled(dialog, 'Save').click();
    (await roleRequest()).flush(
      { title: 'Nook needs at least one admin.', status: 409 },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    expect(text(dialog.querySelector('[role="alert"]'))).toBe('Nook needs at least one admin.');
    expect(document.querySelector('app-change-user-role-dialog')).not.toBeNull();
  });

  it('closes and reloads the list when the user is already gone', async () => {
    const dialog = await openDialog('Grace');

    choose(dialog, 'admin');
    buttonLabelled(dialog, 'Save').click();
    (await roleRequest()).flush(null, { status: 404, statusText: 'Not Found' });

    (await usersRequest(httpTesting)).flush({ items: [ada], total: 1, offset: 0, limit: 50 });
    await settle();
    expect(document.querySelector('app-change-user-role-dialog')).toBeNull();
    expect(document.querySelector('[role="alert"]')).toBeNull();
  });

  it("asks for a role before saving when the user's role is not built in", async () => {
    const dialog = await openDialog('Noor');

    expect(select(dialog).value).toBe('');
    expect(buttonLabelled(dialog, 'Save').disabled).toBe(true);
    choose(dialog, 'viewer');
    await settle();
    expect(buttonLabelled(dialog, 'Save').disabled).toBe(false);
  });

  it('offers no Change role for a role single sign-on sets', async () => {
    const page = await openPage();
    const roleCell = (name: string) =>
      text(
        Array.from(page.querySelectorAll('tbody tr'))
          .find((row) => text(row.querySelector('td span')) === name)!
          .querySelectorAll('td')[2],
      );

    expect(roleCell('Linus')).toContain('Set by single sign-on');
    expect(roleCell('Grace')).not.toContain('Set by single sign-on');

    actionsFor(page, 'Linus').click();
    await vi.waitFor(() => expect(menuItems().length).toBeGreaterThan(0));
    expect(menuItems().map((item) => text(item))).toEqual(['Set password', 'Delete']);
  });
});
