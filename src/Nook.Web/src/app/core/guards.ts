import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Session } from './session';

/** Lets a signed-in user through, and sends a signed-out visitor to setup when Nook has no users, or to sign-in. */
export const signedInGuard: CanActivateFn = async () => {
  const session = inject(Session);
  const router = inject(Router);

  await session.ensureLoaded();
  if (session.user()) {
    return true;
  }
  return router.parseUrl((await session.setupRequired()) ? '/setup' : '/sign-in');
};

/** Lets a signed-out visitor through, and sends a signed-in user to the works list. */
export const signedOutGuard: CanActivateFn = async () => {
  const session = inject(Session);
  const router = inject(Router);

  await session.ensureLoaded();
  return session.user() ? router.parseUrl('/works') : true;
};

/**
 * Lets a signed-out visitor through while Nook has no users. A signed-in user goes to the works list, and a
 * signed-out visitor to an instance that is already set up goes to sign-in.
 */
export const setupGuard: CanActivateFn = async () => {
  const session = inject(Session);
  const router = inject(Router);

  await session.ensureLoaded();
  if (session.user()) {
    return router.parseUrl('/works');
  }
  return (await session.setupRequired()) ? true : router.parseUrl('/sign-in');
};
