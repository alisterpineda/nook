import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { components } from '../../api/schema';
import { buttonLabelled } from '../../testing/button-labelled';
import { text } from '../../testing/element-text';
import { fieldLabelled } from '../../testing/field-labelled';
import { typeInto } from '../../testing/type-into';
import { tooManyAttempts } from '../../core/messages';
import { AccountPage } from './account-page';

type AccountResponse = components['schemas']['AccountResponse'];

describe('AccountPage', () => {
  const ada: AccountResponse = {
    email: 'ada@example.com',
    name: 'Ada Lovelace',
    role: 'Contributor',
    hasPassword: true,
    hasSingleSignOn: false,
  };

  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'account', component: AccountPage }]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => httpTesting.verify());

  async function answerSignInOptions(passwordSignIn: boolean): Promise<void> {
    (await vi.waitFor(() => httpTesting.expectOne('/api/auth/sign-in-options'))).flush({
      ssoName: null,
      passwordSignIn,
    });
  }

  /** Opens the page on `account` and returns it once the account and the sign-in options are answered. */
  async function open(account: AccountResponse, passwordSignIn = true): Promise<HTMLElement> {
    const navigating = harness.navigateByUrl('/account');
    (await vi.waitFor(() => httpTesting.expectOne('/api/account'))).flush(account);
    await answerSignInOptions(passwordSignIn);
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await vi.waitFor(() => expect(text(page)).toContain(account.email));
    return page;
  }

  async function fill(page: HTMLElement, fields: Record<string, string>): Promise<void> {
    for (const [label, value] of Object.entries(fields)) {
      typeInto(fieldLabelled(page, label), value);
    }
    buttonLabelled(page, 'Change password').click();
    await harness.fixture.whenStable();
  }

  it('shows the email, name and role', async () => {
    const navigating = harness.navigateByUrl('/account');
    (await vi.waitFor(() => httpTesting.expectOne('/api/account'))).flush(ada);
    await answerSignInOptions(true);
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;

    await vi.waitFor(() => expect(text(page)).toContain('ada@example.com'));
    expect(text(page)).toContain('Ada Lovelace');
    expect(text(page)).toContain('Contributor');
  });

  it('offers a retry when the account cannot be read', async () => {
    const navigating = harness.navigateByUrl('/account');
    (await vi.waitFor(() => httpTesting.expectOne('/api/account'))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await answerSignInOptions(true);
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;

    await vi.waitFor(() => expect(page.querySelector('app-load-failed')).not.toBeNull());
  });

  it('offers a retry when the sign-in options cannot be read, and then shows the password form', async () => {
    const navigating = harness.navigateByUrl('/account');
    (await vi.waitFor(() => httpTesting.expectOne('/api/account'))).flush(ada);
    (await vi.waitFor(() => httpTesting.expectOne('/api/auth/sign-in-options'))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await vi.waitFor(() => expect(page.querySelector('app-load-failed')).not.toBeNull());

    buttonLabelled(page, 'Retry').click();
    (await vi.waitFor(() => httpTesting.expectOne('/api/account'))).flush(ada);
    await answerSignInOptions(true);

    await vi.waitFor(() => fieldLabelled(page, 'Current password'));
  });

  describe('password form', () => {
    const passwordRequest = () => httpTesting.expectOne('/api/account/password');

    it('sends the current and new password, then says the password was changed', async () => {
      const page = await open(ada);

      await fill(page, {
        'Current password': 'old-password-1',
        'New password': 'new-password-1',
        'Confirm new password': 'new-password-1',
      });
      const request = passwordRequest();
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({
        currentPassword: 'old-password-1',
        newPassword: 'new-password-1',
      });
      request.flush(null, { status: 204, statusText: 'No Content' });
      await harness.fixture.whenStable();

      await vi.waitFor(() => expect(page.querySelector('[role="status"]')).not.toBeNull());
      expect(text(page.querySelector('[role="status"]'))).toContain('Your password was changed.');
      expect(fieldLabelled(page, 'Current password').value).toBe('');
      expect(fieldLabelled(page, 'New password').value).toBe('');
      expect(fieldLabelled(page, 'Confirm new password').value).toBe('');
    });

    it('asks only for a new password when the account has none', async () => {
      const page = await open({ ...ada, hasPassword: false });

      expect(() => fieldLabelled(page, 'Current password')).toThrow();
      expect(text(page.querySelector('h2'))).toBe('Add a password');
      buttonLabelled(page, 'Add password');
      typeInto(fieldLabelled(page, 'New password'), 'new-password-1');
      typeInto(fieldLabelled(page, 'Confirm new password'), 'new-password-1');
      buttonLabelled(page, 'Add password').click();
      await harness.fixture.whenStable();
      const request = passwordRequest();
      expect(request.request.body).toEqual({
        currentPassword: null,
        newPassword: 'new-password-1',
      });
      request.flush(null, { status: 204, statusText: 'No Content' });
      await harness.fixture.whenStable();

      await vi.waitFor(() => fieldLabelled(page, 'Current password'));
      expect(text(page.querySelector('h2'))).toBe('Change your password');
      httpTesting.expectNone('/api/account');
    });

    it('sends nothing when the two new passwords differ', async () => {
      const page = await open(ada);

      await fill(page, {
        'Current password': 'old-password-1',
        'New password': 'new-password-1',
        'Confirm new password': 'new-password-2',
      });

      httpTesting.expectNone('/api/account/password');
      const confirm = fieldLabelled(page, 'Confirm new password');
      expect(text(confirm.parentElement)).toContain('The two passwords do not match.');
    });

    it("shows the API's message on the field it names", async () => {
      const page = await open(ada);
      const filled = {
        'Current password': 'old-password-1',
        'New password': 'new-password-1',
        'Confirm new password': 'new-password-1',
      };

      await fill(page, filled);
      passwordRequest().flush(
        { errors: { CurrentPassword: ['Incorrect password.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      await harness.fixture.whenStable();
      await vi.waitFor(() =>
        expect(text(fieldLabelled(page, 'Current password').parentElement)).toContain(
          'Incorrect password.',
        ),
      );
      expect(text(fieldLabelled(page, 'New password').parentElement)).not.toContain(
        'Incorrect password.',
      );

      await fill(page, filled);
      passwordRequest().flush(
        { errors: { NewPassword: ['Passwords must be at least 12 characters.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      await harness.fixture.whenStable();
      await vi.waitFor(() =>
        expect(text(fieldLabelled(page, 'New password').parentElement)).toContain(
          'Passwords must be at least 12 characters.',
        ),
      );
      expect(text(fieldLabelled(page, 'Current password').parentElement)).not.toContain(
        'Incorrect password.',
      );
    });

    it('shows no password form while password sign-in is off', async () => {
      const page = await open(ada, false);

      expect(page.querySelector('input')).toBeNull();
      expect(text(page)).toContain('Ada Lovelace');
    });

    it('says to wait after too many attempts', async () => {
      const page = await open(ada);

      await fill(page, {
        'Current password': 'old-password-1',
        'New password': 'new-password-1',
        'Confirm new password': 'new-password-1',
      });
      passwordRequest().flush(null, { status: 429, statusText: 'Too Many Requests' });
      await harness.fixture.whenStable();

      await vi.waitFor(() => expect(text(page)).toContain(tooManyAttempts));
    });
  });
});
