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
import { menuItemLabelled } from '../../testing/menu-item-labelled';
import { openUsersPage, usersRequest } from '../../testing/users-page-requests';
import { UsersPage } from './users-page';

type CurrentUserResponse = components['schemas']['CurrentUserResponse'];
type UserSummaryResponse = components['schemas']['UserSummaryResponse'];

describe('DeleteUserDialog', () => {
  const adaId = '0198a1b2-0000-7000-8000-000000000001';
  const graceId = '0198a1b2-0000-7000-8000-000000000002';

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
    role: 'Contributor',
    builtInRole: 'contributor',
    hasAuditEntries: false,
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

  /** Opens the users page, chooses Delete from Grace's menu and returns the dialog. */
  async function openDialog(): Promise<HTMLElement> {
    const page = await openUsersPage(harness, httpTesting, { users: [ada, grace] });
    await settle();
    page.querySelector<HTMLElement>('button[aria-label="Actions for Grace"]')!.click();
    (await vi.waitFor(() => menuItemLabelled('Delete'))).click();
    return vi.waitFor(() => {
      const found = document.querySelector<HTMLElement>('app-delete-user-dialog');
      expect(found).not.toBeNull();
      return found!;
    });
  }

  function deleteRequest() {
    return vi.waitFor(() => httpTesting.expectOne(`/api/users/${graceId}`));
  }

  it('names the user it would delete', async () => {
    const dialog = await openDialog();

    expect(text(dialog)).toContain('Grace');
    expect(text(dialog)).toContain('grace@example.com');
    expect(text(dialog)).toContain('books they added stay');
    expect(text(dialog)).toContain('This cannot be undone.');
  });

  it('deletes the user and reloads the list', async () => {
    const dialog = await openDialog();

    buttonLabelled(dialog, 'Delete').click();
    const request = await deleteRequest();
    expect(request.request.method).toBe('DELETE');
    await settle();
    expect(buttonLabelled(dialog, 'Delete').disabled).toBe(true);
    request.flush(null, { status: 204, statusText: 'No Content' });

    const reload = await usersRequest(httpTesting);
    expect(reload.request.method).toBe('GET');
    reload.flush({ items: [ada], total: 1, offset: 0, limit: 50 });
    await settle();
    expect(document.querySelector('app-delete-user-dialog')).toBeNull();
  });

  it('sends nothing on Cancel', async () => {
    const dialog = await openDialog();

    buttonLabelled(dialog, 'Cancel').click();
    await settle();

    expect(document.querySelector('app-delete-user-dialog')).toBeNull();
  });

  it('treats a user who is already gone as deleted', async () => {
    const dialog = await openDialog();

    buttonLabelled(dialog, 'Delete').click();
    (await deleteRequest()).flush(null, { status: 404, statusText: 'Not Found' });

    (await usersRequest(httpTesting)).flush({ items: [ada], total: 1, offset: 0, limit: 50 });
    await settle();
    expect(document.querySelector('app-delete-user-dialog')).toBeNull();
  });

  it('shows why the API refused', async () => {
    const dialog = await openDialog();

    buttonLabelled(dialog, 'Delete').click();
    (await deleteRequest()).flush(
      { title: 'Nook needs at least one admin.', status: 409 },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    expect(text(dialog.querySelector('[role="alert"]'))).toBe('Nook needs at least one admin.');
    expect(document.querySelector('app-delete-user-dialog')).not.toBeNull();
    expect(buttonLabelled(dialog, 'Delete').disabled).toBe(false);
  });

  it('says something went wrong on any other failure', async () => {
    const dialog = await openDialog();

    buttonLabelled(dialog, 'Delete').click();
    (await deleteRequest()).flush(null, { status: 500, statusText: 'Server Error' });
    await settle();

    expect(text(dialog.querySelector('[role="alert"]'))).toBe('Something went wrong. Try again.');
    expect(document.querySelector('app-delete-user-dialog')).not.toBeNull();
  });
});
