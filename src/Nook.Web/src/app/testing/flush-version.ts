import { HttpTestingController } from '@angular/common/http/testing';

/** Answers every open request for Nook's version, for specs that are not about it. */
export function flushVersion(httpTesting: HttpTestingController): void {
  for (const request of httpTesting.match('/api/version')) {
    request.flush({ version: '0.0.0-dev+abc1234' });
  }
}
