import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { RouterTestingHarness } from '@angular/router/testing';
import { vi } from 'vitest';
import { components } from '../api/schema';

type UserSummaryResponse = components['schemas']['UserSummaryResponse'];

/** How a spec opens the users page. */
export interface UsersPageOpening {
  /** The users the list answers with. */
  users: UserSummaryResponse[];
  /** How many users there are in all: as many as `users` unless said. */
  total?: number;
  /** Where to open the page: `/users` unless said. */
  url?: string;
  /** Whether Nook signs in with passwords: it does unless said. */
  passwordSignIn?: boolean;
}

/** The users page's open request for the list or for adding a user, for specs. Waits until it is made. */
export function usersRequest(httpTesting: HttpTestingController): Promise<TestRequest> {
  return vi.waitFor(() => httpTesting.expectOne((request) => request.url === '/api/users'));
}

/** Answers the users page's request for the sign-in options, for specs. */
export async function answerSignInOptions(
  httpTesting: HttpTestingController,
  passwordSignIn = true,
): Promise<void> {
  (await vi.waitFor(() => httpTesting.expectOne('/api/auth/sign-in-options'))).flush({
    ssoName: null,
    passwordSignIn,
  });
}

/** Opens the users page for specs, answering its requests for the sign-in options and the list, and returns it once it shows the list. */
export async function openUsersPage(
  harness: RouterTestingHarness,
  httpTesting: HttpTestingController,
  { users, total = users.length, url = '/users', passwordSignIn = true }: UsersPageOpening,
): Promise<HTMLElement> {
  const navigating = harness.navigateByUrl(url);
  await answerSignInOptions(httpTesting, passwordSignIn);
  (await usersRequest(httpTesting)).flush({ items: users, total, offset: 0, limit: 50 });
  await navigating;
  const page = harness.routeNativeElement as HTMLElement;
  await vi.waitFor(() => {
    if (!page.querySelector('table, [role="alert"]')) {
      throw new Error('The users page shows no list yet');
    }
  });
  return page;
}
