import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from './app.config';
import { FakeActivityHub, provideFakeActivityHub } from './testing/fake-activity-hub';

describe('appConfig', () => {
  const originalMatchMedia = window.matchMedia;

  afterEach(() => {
    window.matchMedia = originalMatchMedia;
    document.documentElement.classList.remove('dark');
    localStorage.clear();
  });

  it('starts looking for a newer build when the app starts', () => {
    window.matchMedia = vi.fn((query: string) => ({
      matches: false,
      media: query,
      addEventListener: () => undefined,
    })) as unknown as typeof window.matchMedia;
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting(), provideFakeActivityHub()],
    });

    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(FakeActivityHub).reconnected.next();

    http.expectOne('/index.html');
  });
});
