import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { components } from '../api/schema';
import { Session } from './session';

type CurrentUserResponse = components['schemas']['CurrentUserResponse'];

describe('Session', () => {
  const ada: CurrentUserResponse = {
    id: '0198a1b2-0000-7000-8000-000000000001',
    email: 'ada@example.com',
    role: 'admin',
  };

  let session: Session;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    session = TestBed.inject(Session);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('load sets the user when the API knows the session', async () => {
    const loading = session.load();

    const request = httpTesting.expectOne('/api/auth/me');
    expect(request.request.method).toBe('GET');
    request.flush(ada);
    await loading;

    expect(session.user()).toEqual(ada);
  });

  it('load leaves the user empty on 401', async () => {
    const loading = session.load();

    httpTesting.expectOne('/api/auth/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    await loading;

    expect(session.user()).toBeNull();
  });

  it('isAdmin is true only while an admin is signed in', async () => {
    expect(session.isAdmin()).toBe(false);

    for (const [role, admin] of [
      ['admin', true],
      ['contributor', false],
      ['viewer', false],
    ] as const) {
      const loading = session.load();
      httpTesting.expectOne('/api/auth/me').flush({ ...ada, role });
      await loading;

      expect(session.isAdmin()).toBe(admin);
    }
  });

  it('load passes on an error other than 401', async () => {
    const loading = session.load();

    httpTesting
      .expectOne('/api/auth/me')
      .flush(null, { status: 500, statusText: 'Internal Server Error' });

    await expect(loading).rejects.toMatchObject({ status: 500 });
    expect(session.user()).toBeNull();
  });

  it('ensureLoaded asks the API once and reuses the answer', async () => {
    const first = session.ensureLoaded();
    const second = session.ensureLoaded();

    httpTesting.expectOne('/api/auth/me').flush(ada);
    await Promise.all([first, second]);
    await session.ensureLoaded();

    httpTesting.expectNone('/api/auth/me');
    expect(session.user()).toEqual(ada);
  });

  it('ensureLoaded asks again after a failed load', async () => {
    const failing = session.ensureLoaded();
    httpTesting
      .expectOne('/api/auth/me')
      .flush(null, { status: 500, statusText: 'Internal Server Error' });
    await expect(failing).rejects.toMatchObject({ status: 500 });

    const retrying = session.ensureLoaded();
    httpTesting.expectOne('/api/auth/me').flush(ada);
    await retrying;

    expect(session.user()).toEqual(ada);
  });

  it('signIn posts the email and password and sets the user', async () => {
    const signingIn = session.signIn('ada@example.com', 'correct horse');

    const request = httpTesting.expectOne('/api/auth/login');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: 'ada@example.com', password: 'correct horse' });
    request.flush(ada);
    await signingIn;

    expect(session.user()).toEqual(ada);
  });

  it('signIn passes on a refusal and leaves the user empty', async () => {
    const signingIn = session.signIn('ada@example.com', 'wrong');

    httpTesting
      .expectOne('/api/auth/login')
      .flush(
        { title: 'The email or password is incorrect.', status: 401 },
        { status: 401, statusText: 'Unauthorized' },
      );

    await expect(signingIn).rejects.toMatchObject({
      status: 401,
      error: { title: 'The email or password is incorrect.' },
    });
    expect(session.user()).toBeNull();
  });

  it('signOut posts to logout and clears the user', async () => {
    const signingIn = session.signIn('ada@example.com', 'correct horse');
    httpTesting.expectOne('/api/auth/login').flush(ada);
    await signingIn;

    const signingOut = session.signOut();

    const request = httpTesting.expectOne('/api/auth/logout');
    expect(request.request.method).toBe('POST');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await signingOut;

    expect(session.user()).toBeNull();
  });

  it('signOut clears the user when the API says there is no session to end', async () => {
    const signingIn = session.signIn('ada@example.com', 'correct horse');
    httpTesting.expectOne('/api/auth/login').flush(ada);
    await signingIn;

    const signingOut = session.signOut();
    httpTesting
      .expectOne('/api/auth/logout')
      .flush(null, { status: 401, statusText: 'Unauthorized' });
    await signingOut;

    expect(session.user()).toBeNull();
  });

  it('signOut keeps the user and passes on the error when the API fails', async () => {
    const signingIn = session.signIn('ada@example.com', 'correct horse');
    httpTesting.expectOne('/api/auth/login').flush(ada);
    await signingIn;

    const signingOut = session.signOut();
    httpTesting
      .expectOne('/api/auth/logout')
      .flush(null, { status: 500, statusText: 'Internal Server Error' });

    await expect(signingOut).rejects.toMatchObject({ status: 500 });
    expect(session.user()).toEqual(ada);
  });

  it('setupRequired reports what the API says', async () => {
    const required = session.setupRequired();
    const request = httpTesting.expectOne('/api/setup');
    expect(request.request.method).toBe('GET');
    request.flush({ required: true });
    expect(await required).toBe(true);

    const notRequired = session.setupRequired();
    httpTesting.expectOne('/api/setup').flush({ required: false });
    expect(await notRequired).toBe(false);
  });

  it('signInOptions reports what the API says', async () => {
    const named = session.signInOptions();
    const request = httpTesting.expectOne('/api/auth/sign-in-options');
    expect(request.request.method).toBe('GET');
    request.flush({ ssoName: 'Authentik' });
    expect(await named).toEqual({ ssoName: 'Authentik' });

    const unnamed = session.signInOptions();
    httpTesting.expectOne('/api/auth/sign-in-options').flush({ ssoName: null });
    expect(await unnamed).toEqual({ ssoName: null });
  });

  it('createFirstAdmin posts the email and password and sets the user', async () => {
    const creating = session.createFirstAdmin('ada@example.com', 'correct horse');

    const request = httpTesting.expectOne('/api/setup');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: 'ada@example.com', password: 'correct horse' });
    request.flush(ada);
    await creating;

    expect(session.user()).toEqual(ada);
  });
});
