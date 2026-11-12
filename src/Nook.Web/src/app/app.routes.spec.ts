import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { components } from './api/schema';
import { routes } from './app.routes';
import { SetupPage } from './features/setup/setup-page';
import { SignInPage } from './features/sign-in/sign-in-page';
import { WorkDetailPage } from './features/works/work-detail-page';
import { WorksListPage } from './features/works/works-list-page';

type CurrentUserResponse = components['schemas']['CurrentUserResponse'];
type PageOfWorkSummaryResponse = components['schemas']['PageOfWorkSummaryResponse'];
type WorkDetailResponse = components['schemas']['WorkDetailResponse'];

describe('routes', () => {
  const ada: CurrentUserResponse = {
    id: '0198a1b2-0000-7000-8000-000000000001',
    email: 'ada@example.com',
    role: 'admin',
  };
  const noWorks: PageOfWorkSummaryResponse = { items: [], total: 0, offset: 0, limit: 50 };
  const dune: WorkDetailResponse = {
    id: '42',
    libraryId: '0198a1b2-0000-7000-8000-00000000000a',
    title: 'Dune',
    sortTitle: 'Dune',
    description: null,
    language: null,
    publicationDate: null,
    publisher: null,
    series: null,
    coverHash: null,
    contributors: [],
    tags: [],
    externalIds: [],
    items: [],
    addedAt: '2026-11-01T12:00:00Z',
    updatedAt: '2026-11-01T12:00:00Z',
  };

  /** What the API answers on each path, for a visitor who is signed in or not on an instance that is set up or not. */
  function answers(visitor: { signedIn: boolean; setupRequired: boolean }): Record<string, object> {
    return {
      '/api/setup': { required: visitor.setupRequired },
      '/api/libraries': [],
      '/api/works': noWorks,
      '/api/works/42': dune,
      ...(visitor.signedIn ? { '/api/auth/me': ada } : {}),
    };
  }

  /**
   * Opens `url` as `visitor`, answering each request the guards and pages make along the way, and returns the page
   * of type `page` that ends up on screen, if there is one.
   */
  async function open<T>(
    url: string,
    visitor: { signedIn: boolean; setupRequired: boolean },
    page: Type<T>,
  ): Promise<{ landedOn: string; shown: boolean }> {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    });
    const httpTesting = TestBed.inject(HttpTestingController);
    const harness = await RouterTestingHarness.create();
    const byPath = answers(visitor);

    let settled = false;
    const navigating = harness.navigateByUrl(url).finally(() => (settled = true));
    const isShown = () => harness.fixture.debugElement.query(By.directive(page)) !== null;
    await vi.waitFor(() => {
      for (const request of httpTesting.match(() => true)) {
        const body = byPath[request.request.url];
        if (body) {
          request.flush(body);
        } else {
          request.flush(null, { status: 401, statusText: 'Unauthorized' });
        }
      }
      harness.detectChanges();
      expect(settled && isShown()).toBe(true);
    });
    await navigating;
    httpTesting.verify();

    return { landedOn: TestBed.inject(Router).url, shown: isShown() };
  }

  it('the screens are at /setup, /sign-in, /works and /works/:id', async () => {
    const firstVisitor = { signedIn: false, setupRequired: true };
    const signedOut = { signedIn: false, setupRequired: false };
    const signedIn = { signedIn: true, setupRequired: false };

    expect(await open('/setup', firstVisitor, SetupPage)).toEqual({
      landedOn: '/setup',
      shown: true,
    });
    expect(await open('/sign-in', signedOut, SignInPage)).toEqual({
      landedOn: '/sign-in',
      shown: true,
    });
    expect(await open('/works', signedIn, WorksListPage)).toEqual({
      landedOn: '/works',
      shown: true,
    });
    expect(await open('/works/42', signedIn, WorkDetailPage)).toEqual({
      landedOn: '/works/42',
      shown: true,
    });
  });
});
