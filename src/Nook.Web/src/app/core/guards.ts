import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { Session } from './session';

/** Where a visitor who may not enter goes: setup when Nook has no users, or sign-in. */
async function setupOrSignIn(session: Session, router: Router): Promise<UrlTree> {
  return router.parseUrl((await session.setupRequired()) ? '/setup' : '/sign-in');
}

/**
 * Lets a signed-in user through, and a visitor nobody has signed in as when public access is on. Any other visitor
 * goes to setup when Nook has no users, or to sign-in.
 */
export const libraryGuard: CanActivateFn = async () => {
  const session = inject(Session);
  const router = inject(Router);

  await session.ensureLoaded();
  if (session.user() || session.publicAccess()) {
    return true;
  }
  return setupOrSignIn(session, router);
};

/** Lets a signed-out visitor through, and sends a signed-in user to the works list. */
export const signedOutGuard: CanActivateFn = async () => {
  const session = inject(Session);
  const router = inject(Router);

  await session.ensureLoaded();
  return session.user() ? router.parseUrl('/library') : true;
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
    return router.parseUrl('/library');
  }
  return (await session.setupRequired()) ? true : router.parseUrl('/sign-in');
};

/**
 * Lets an admin through, and sends anyone else to the works list. It sits below `libraryGuard`, which has already
 * sent away a visitor who may not enter the library.
 */
export const adminGuard: CanActivateFn = async () => {
  const session = inject(Session);
  const router = inject(Router);

  await session.ensureLoaded();
  return session.isAdmin() ? true : router.parseUrl('/library');
};

/**
 * Lets a signed-in user through, and sends anyone else to setup when Nook has no users, or to sign-in. Unlike
 * `libraryGuard` it turns a visitor away while public access is on, for pages that are about the signed-in user.
 */
export const signedInGuard: CanActivateFn = async () => {
  const session = inject(Session);
  const router = inject(Router);

  await session.ensureLoaded();
  if (session.user()) {
    return true;
  }
  return setupOrSignIn(session, router);
};
