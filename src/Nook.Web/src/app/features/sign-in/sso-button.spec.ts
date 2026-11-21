import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { text } from '../../testing/element-text';
import { SsoButton } from './sso-button';

describe('SsoButton', () => {
  let fixture: ComponentFixture<SsoButton>;
  let button: HTMLElement;
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SsoButton);
    button = fixture.nativeElement;
    await fixture.whenStable();
  });

  afterEach(() => httpTesting.verify());

  async function answer(body: object | null, status = 200): Promise<void> {
    const request = httpTesting.expectOne('/api/auth/sign-in-options');
    expect(request.request.method).toBe('GET');
    request.flush(body, { status, statusText: status === 200 ? 'OK' : 'Internal Server Error' });
    await fixture.whenStable();
  }

  it('shows a link named after the provider', async () => {
    await answer({ ssoName: 'Authentik', passwordSignIn: true });

    const link = await vi.waitFor(() => {
      const found = button.querySelector('a');
      expect(found).not.toBeNull();
      return found!;
    });
    expect(text(link)).toBe('Sign in with Authentik');
    expect(link.getAttribute('href')).toBe('/api/auth/oidc/login');
  });

  it('shows nothing while the request is open', () => {
    expect(button.children.length).toBe(0);
    expect(text(button)).toBe('');

    httpTesting.expectOne('/api/auth/sign-in-options');
  });

  it('shows nothing when there is no single sign-on', async () => {
    await answer({ ssoName: null, passwordSignIn: true });

    expect(button.children.length).toBe(0);
    expect(text(button)).toBe('');
  });

  it('shows nothing when the request fails', async () => {
    await answer(null, 500);

    expect(button.children.length).toBe(0);
    expect(text(button)).toBe('');
  });
});
