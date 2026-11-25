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
import { typeInto } from '../../testing/type-into';
import { openUsersPage, usersRequest } from '../../testing/users-page-requests';
import { UsersPage } from './users-page';

type CurrentUserResponse = components['schemas']['CurrentUserResponse'];
type UserSummaryResponse = components['schemas']['UserSummaryResponse'];

describe('AddUserDialog', () => {
  const adaId = '0198a1b2-0000-7000-8000-000000000001';

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
    const page = await openUsersPage(harness, httpTesting, { users: [ada], passwordSignIn });
    await settle();
    return page;
  }

  /** Opens the page, presses Add user and returns the dialog. */
  async function openDialog(): Promise<HTMLElement> {
    const page = await openPage();
    buttonLabelled(page, 'Add user').click();
    return vi.waitFor(() => {
      const found = document.querySelector<HTMLElement>('app-add-user-dialog');
      expect(found).not.toBeNull();
      return found!;
    });
  }

  function fill(dialog: HTMLElement, email: string, name: string, password: string): void {
    typeInto(fieldLabelled(dialog, 'Email'), email);
    typeInto(fieldLabelled(dialog, 'Name'), name);
    typeInto(fieldLabelled(dialog, 'Password'), password);
  }

  function addRequest(): Promise<TestRequest> {
    return vi.waitFor(() =>
      httpTesting.expectOne((request) => request.url === '/api/users' && request.method === 'POST'),
    );
  }

  it('starts on Viewer and offers admin, contributor and viewer', async () => {
    const dialog = await openDialog();

    const role = fieldLabelled(dialog, 'Role', HTMLSelectElement);
    expect(Array.from(role.options, (option) => text(option))).toEqual([
      'Admin',
      'Contributor',
      'Viewer',
    ]);
    expect(role.value).toBe('viewer');
  });

  it('adds the user and shows them in the list', async () => {
    const dialog = await openDialog();

    fill(dialog, 'grace@example.com', 'Grace', 'a-long-password');
    const role = fieldLabelled(dialog, 'Role', HTMLSelectElement);
    role.value = 'contributor';
    role.dispatchEvent(new Event('change'));
    buttonLabelled(dialog, 'Add user').click();

    const request = await addRequest();
    expect(request.request.body).toEqual({
      email: 'grace@example.com',
      displayName: 'Grace',
      role: 'contributor',
      password: 'a-long-password',
    });
    request.flush({ ...ada, id: 'new', name: 'Grace', email: 'grace@example.com' });

    const reload = await usersRequest(httpTesting);
    expect(reload.request.method).toBe('GET');
    reload.flush({ items: [ada], total: 1, offset: 0, limit: 50 });
    await settle();
    expect(document.querySelector('app-add-user-dialog')).toBeNull();
  });

  it('leaves the name out when it is blank', async () => {
    const dialog = await openDialog();

    fill(dialog, 'grace@example.com', '   ', 'a-long-password');
    buttonLabelled(dialog, 'Add user').click();

    const request = await addRequest();
    expect(request.request.body).toEqual({
      email: 'grace@example.com',
      displayName: null,
      role: 'viewer',
      password: 'a-long-password',
    });
    request.flush({ ...ada, id: 'new' });
    (await usersRequest(httpTesting)).flush({ items: [ada], total: 1, offset: 0, limit: 50 });
  });

  it("shows the API's message on the field it names", async () => {
    const dialog = await openDialog();

    fill(dialog, 'ada@example.com', '', 'short');
    buttonLabelled(dialog, 'Add user').click();
    (await addRequest()).flush(
      {
        errors: {
          Email: ['Email is already taken.'],
          Password: ['Passwords must be at least 12 characters.'],
        },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    const messageBy = (label: string) =>
      text(fieldLabelled(dialog, label).parentElement!.querySelector('[role="alert"]'));
    expect(messageBy('Email')).toBe('Email is already taken.');
    expect(messageBy('Password')).toBe('Passwords must be at least 12 characters.');
    expect(document.querySelector('app-add-user-dialog')).not.toBeNull();
  });

  it('says something went wrong on any other failure', async () => {
    const dialog = await openDialog();

    fill(dialog, 'grace@example.com', '', 'a-long-password');
    buttonLabelled(dialog, 'Add user').click();
    (await addRequest()).flush(null, { status: 500, statusText: 'Server Error' });
    await settle();

    expect(text(dialog.querySelector('[role="alert"]'))).toBe('Something went wrong. Try again.');
    expect(document.querySelector('app-add-user-dialog')).not.toBeNull();
    expect(buttonLabelled(dialog, 'Add user').disabled).toBe(false);
  });

  it('sends nothing when cancelled', async () => {
    const dialog = await openDialog();

    buttonLabelled(dialog, 'Cancel').click();
    await settle();

    expect(document.querySelector('app-add-user-dialog')).toBeNull();
  });

  it('offers no Add user while password sign-in is off', async () => {
    const page = await openPage(false);

    expect(Array.from(page.querySelectorAll('button'), (b) => text(b))).not.toContain('Add user');
  });
});
