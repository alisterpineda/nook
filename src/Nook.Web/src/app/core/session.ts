import { HttpClient, HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { components } from '../api/schema';

type CurrentUserResponse = components['schemas']['CurrentUserResponse'];
type LoginRequest = components['schemas']['LoginRequest'];
type SetupRequest = components['schemas']['SetupRequest'];
type SetupStatusResponse = components['schemas']['SetupStatusResponse'];

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
  private loaded: Promise<void> | null = null;

  /** The signed-in user, or `null` when signed out. */
  readonly user = this.currentUser.asReadonly();

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

  /** Asks the API who the session belongs to. */
  async load(): Promise<void> {
    try {
      this.currentUser.set(
        await firstValueFrom(this.http.get<CurrentUserResponse>('/api/auth/me')),
      );
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === HttpStatusCode.Unauthorized) {
        this.currentUser.set(null);
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

  /** Creates the first admin, whom the API signs in. */
  async createFirstAdmin(email: string, password: string): Promise<void> {
    const body: SetupRequest = { email, password };
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
   * any other failure the `nook` cookie still works, so the app must not look signed out.
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
  }
}
