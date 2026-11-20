import { HttpClient, HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { components } from '../api/schema';
import { ContentAccess } from './content-access';

type AccessResponse = components['schemas']['AccessResponse'];
type CurrentUserResponse = components['schemas']['CurrentUserResponse'];
type LoginRequest = components['schemas']['LoginRequest'];
type SetupRequest = components['schemas']['SetupRequest'];
type SetupStatusResponse = components['schemas']['SetupStatusResponse'];
type SignInOptionsResponse = components['schemas']['SignInOptionsResponse'];

/** What a visitor may do until the API says otherwise: nothing. */
const noVisitorAccess: AccessResponse = { publicAccess: false, defaultArea: 'none' };

/**
 * Who is signed in, and the calls that change it. The session itself is the API's `nook` cookie, so the user
 * here is only what the API last said about it.
 *
 * Errors other than the 401 that `load()` takes to mean "signed out" reach the caller as the `HttpErrorResponse`.
 */
@Injectable({ providedIn: 'root' })
export class Session {
  private readonly http = inject(HttpClient);
  private readonly currentUser = signal<CurrentUserResponse | null>(null);
  private readonly visitorAccess = signal(noVisitorAccess);
  private loaded: Promise<void> | null = null;

  /** The signed-in user, or `null` when signed out. */
  readonly user = this.currentUser.asReadonly();

  /** Whether the signed-in user is an admin, who alone may add books. */
  readonly isAdmin = computed(() => this.currentUser()?.role === 'admin');

  /** Whether someone who is not signed in may enter the library. Read only while nobody is signed in. */
  readonly publicAccess = computed(() => this.visitorAccess().publicAccess);

  /**
   * The caller's level in the Default area: `'download'` for a signed-in user, and for a visitor what the API
   * last said.
   */
  readonly defaultAreaAccess = computed<ContentAccess>(() =>
    this.currentUser() ? 'download' : this.visitorAccess().defaultArea,
  );

  /**
   * Calls `load()` the first time it is asked and hands back that same answer after, so the route guards can
   * know the session on a fresh page load without asking the API on every navigation. A failed load is not kept,
   * so the next call asks again.
   */
  ensureLoaded(): Promise<void> {
    this.loaded ??= this.load().catch((error: unknown) => {
      this.loaded = null;
      throw error;
    });
    return this.loaded;
  }

  /** Asks the API who the session belongs to, and when nobody is signed in, what a visitor may do. */
  async load(): Promise<void> {
    try {
      this.currentUser.set(
        await firstValueFrom(this.http.get<CurrentUserResponse>('/api/auth/me')),
      );
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === HttpStatusCode.Unauthorized) {
        this.currentUser.set(null);
        await this.readAccess();
        return;
      }
      throw error;
    }
  }

  /** Whether Nook still needs its first admin. */
  async setupRequired(): Promise<boolean> {
    const status = await firstValueFrom(this.http.get<SetupStatusResponse>('/api/setup'));
    return status.required;
  }

  /** The ways to sign in besides a password: the name of the single sign-on provider, or `null` when there is none. */
  signInOptions(): Promise<SignInOptionsResponse> {
    return firstValueFrom(this.http.get<SignInOptionsResponse>('/api/auth/sign-in-options'));
  }

  /** Creates the first admin, whom the API signs in. */
  async createFirstAdmin(email: string, password: string, displayName = ''): Promise<void> {
    const body: SetupRequest = { email, password, displayName: displayName.trim() || undefined };
    this.currentUser.set(
      await firstValueFrom(this.http.post<CurrentUserResponse>('/api/setup', body)),
    );
  }

  async signIn(email: string, password: string): Promise<void> {
    const body: LoginRequest = { email, password };
    this.currentUser.set(
      await firstValueFrom(this.http.post<CurrentUserResponse>('/api/auth/login', body)),
    );
  }

  /**
   * Ends the session. The user is cleared only once the API has ended it, or says there was none to end: after
   * any other failure the `nook` cookie still works, so the app must not look signed out. What a visitor may do
   * is then read again; when that read fails the session is ended all the same, with public access taken as off.
   */
  async signOut(): Promise<void> {
    try {
      await firstValueFrom(this.http.post<void>('/api/auth/logout', null));
    } catch (error) {
      if (!(error instanceof HttpErrorResponse) || error.status !== HttpStatusCode.Unauthorized) {
        throw error;
      }
    }
    this.currentUser.set(null);
    try {
      await this.readAccess();
    } catch {
      this.visitorAccess.set(noVisitorAccess);
    }
  }

  private async readAccess(): Promise<void> {
    this.visitorAccess.set(await firstValueFrom(this.http.get<AccessResponse>('/api/access')));
  }
}
