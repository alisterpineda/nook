import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { components } from '../api/schema';
import { Session } from '../core/session';

type CurrentUserResponse = components['schemas']['CurrentUserResponse'];

/** Loads the session with `user` signed in, for specs. */
export async function signInAs(user: CurrentUserResponse): Promise<void> {
  const loading = TestBed.inject(Session).load();
  TestBed.inject(HttpTestingController).expectOne('/api/auth/me').flush(user);
  await loading;
}
