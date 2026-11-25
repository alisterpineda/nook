import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { components } from '../../api/schema';
import { Session } from '../../core/session';
import { buttonLabelled } from '../../testing/button-labelled';
import { text } from '../../testing/element-text';
import { fieldLabelled } from '../../testing/field-labelled';
import { menuItemLabelled } from '../../testing/menu-item-labelled';
import { typeInto } from '../../testing/type-into';
import { openUsersPage, usersRequest } from '../../testing/users-page-requests';
import { UsersPage } from './users-page';

type CurrentUserResponse = components['schemas']['CurrentUserResponse'];
type UserSummaryResponse = components['schemas']['UserSummaryResponse'];

describe('SetUserPasswordDialog', () => {
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

  /** Opens the users page, answering the list and the sign-in options. */
  async function openPage(passwordSignIn = true): Promise<HTMLElement> {
    const page = await openUsersPage(harness, httpTesting, { users: [ada, grace], passwordSignIn });
    await settle();
    return page;
  }

  function actionsFor(page: HTMLElement, name: string): HTMLElement | null {
    return page.querySelector<HTMLElement>(`button[aria-label="Actions for ${name}"]`);
  }

  /** Opens the page, chooses Set password from Grace's menu and returns the dialog. */
  async function openDialog(): Promise<HTMLElement> {
    const page = await openPage();
    actionsFor(page, 'Grace')!.click();
    (await vi.waitFor(() => menuItemLabelled('Set password'))).click();
    return vi.waitFor(() => {
      const found = document.querySelector<HTMLElement>('app-set-user-password-dialog');
      expect(found).not.toBeNull();
      return found!;
    });
  }

  function passwordRequest(): Promise<TestRequest> {
    return vi.waitFor(() => httpTesting.expectOne(`/api/users/${graceId}/password`));
  }

  it('sets the password, says whose it was and reloads the list', async () => {
    const dialog = await openDialog();

    expect(text(dialog)).toContain('Grace');
    typeInto(fieldLabelled(dialog, 'Password'), 'a-long-password');
    buttonLabelled(dialog, 'Set password').click();

    const request = await passwordRequest();
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ password: 'a-long-password' });
    request.flush(null, { status: 204, statusText: 'No Content' });

    const reload = await usersRequest(httpTesting);
    expect(reload.request.method).toBe('GET');
    reload.flush({ items: [ada, { ...grace, hasPassword: true }], total: 2, offset: 0, limit: 50 });
    await settle();
    expect(document.querySelector('app-set-user-password-dialog')).toBeNull();
  });

  it("shows the API's message when the password is refused", async () => {
    const dialog = await openDialog();

    typeInto(fieldLabelled(dialog, 'Password'), 'short');
    buttonLabelled(dialog, 'Set password').click();
    (await passwordRequest()).flush(
      { errors: { Password: ['Passwords must be at least 12 characters.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(
      text(fieldLabelled(dialog, 'Password').parentElement!.querySelector('[role="alert"]')),
    ).toBe('Passwords must be at least 12 characters.');
    expect(document.querySelector('app-set-user-password-dialog')).not.toBeNull();
  });

  it('says something went wrong on any other failure', async () => {
    const dialog = await openDialog();

    typeInto(fieldLabelled(dialog, 'Password'), 'a-long-password');
    buttonLabelled(dialog, 'Set password').click();
    (await passwordRequest()).flush(null, { status: 500, statusText: 'Server Error' });
    await settle();

    expect(text(dialog.querySelector('[role="alert"]'))).toBe('Something went wrong. Try again.');
    expect(document.querySelector('app-set-user-password-dialog')).not.toBeNull();
    expect(buttonLabelled(dialog, 'Set password').disabled).toBe(false);
  });

  it('closes and reloads the list when the user is already gone', async () => {
    const dialog = await openDialog();

    typeInto(fieldLabelled(dialog, 'Password'), 'a-long-password');
    buttonLabelled(dialog, 'Set password').click();
    (await passwordRequest()).flush(null, { status: 404, statusText: 'Not Found' });

    const reload = await usersRequest(httpTesting);
    expect(reload.request.method).toBe('GET');
    reload.flush({ items: [ada], total: 1, offset: 0, limit: 50 });
    await settle();
    expect(document.querySelector('app-set-user-password-dialog')).toBeNull();
    expect(document.querySelector('[role="alert"]')).toBeNull();
  });

  it('sends nothing when cancelled', async () => {
    const dialog = await openDialog();

    buttonLabelled(dialog, 'Cancel').click();
    await settle();

    expect(document.querySelector('app-set-user-password-dialog')).toBeNull();
  });

  it('offers no actions on your own row', async () => {
    const page = await openPage();

    expect(actionsFor(page, 'Ada')).toBeNull();
    expect(actionsFor(page, 'Grace')).not.toBeNull();
  });

  it('offers no Set password while password sign-in is off', async () => {
    const page = await openPage(false);

    expect(actionsFor(page, 'Grace')).toBeNull();
    expect(actionsFor(page, 'Ada')).toBeNull();
  });
});
