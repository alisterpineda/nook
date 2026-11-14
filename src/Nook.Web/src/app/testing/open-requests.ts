import { HttpTestingController, TestRequest } from '@angular/common/http/testing';

/**
 * Waits, for specs, until `count` requests for `url` have been made and hands them back unanswered, for pages that
 * ask for one thing from several places at once. `match` takes the requests it finds off the open list, so they are
 * collected across tries.
 */
export async function openRequests(
  httpTesting: HttpTestingController,
  url: string,
  count: number,
): Promise<TestRequest[]> {
  const found: TestRequest[] = [];
  for (let tries = 0; tries < 100; tries++) {
    found.push(...httpTesting.match(url));
    if (found.length >= count) {
      break;
    }
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
  if (found.length !== count) {
    throw new Error(`Expected ${count} requests for ${url}, found ${found.length}`);
  }
  return found;
}
