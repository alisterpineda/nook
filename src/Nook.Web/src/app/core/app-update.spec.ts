import { DOCUMENT } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { FakeActivityHub, provideFakeActivityHub } from '../testing/fake-activity-hub';
import { AppUpdate } from './app-update';

function page(script: string, stylesheet: string): string {
  return `<!doctype html><html><head>
    <link rel="stylesheet" href="${stylesheet}">
  </head><body><app-root></app-root>
    <script src="${script}" type="module"></script>
  </body></html>`;
}

describe('AppUpdate', () => {
  let added: Element[];
  let http: HttpTestingController;

  function setUp(): AppUpdate {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideFakeActivityHub()],
    });
    http = TestBed.inject(HttpTestingController);
    return TestBed.inject(AppUpdate);
  }

  async function checkServing(update: AppUpdate, served: string): Promise<void> {
    const done = update.check();
    http.expectOne('/index.html').flush(served);
    await done;
  }

  function stubVisibility(state: 'visible' | 'hidden') {
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue(state);
  }

  beforeEach(() => {
    const script = document.createElement('script');
    script.setAttribute('src', 'main-AAAA.js');
    script.setAttribute('type', 'module');
    const link = document.createElement('link');
    link.setAttribute('rel', 'stylesheet');
    link.setAttribute('href', 'styles-AAAA.css');
    document.head.append(script, link);
    added = [script, link];
  });

  afterEach(() => {
    added.forEach((element) => element.remove());
    vi.restoreAllMocks();
  });

  it('says an update is available when the served page names a script the running page lacks', async () => {
    const update = setUp();
    expect(update.available()).toBe(false);

    await checkServing(update, page('main-BBBB.js', 'styles-AAAA.css'));

    expect(update.available()).toBe(true);
  });

  it('says an update is available when only the stylesheet changed', async () => {
    const update = setUp();

    await checkServing(update, page('main-AAAA.js', 'styles-BBBB.css'));

    expect(update.available()).toBe(true);
  });

  it('says nothing when the served page names the files the running page has', async () => {
    const update = setUp();

    await checkServing(update, page('main-AAAA.js', 'styles-AAAA.css'));

    expect(update.available()).toBe(false);
  });

  it('says nothing when the served text is not a page of scripts and stylesheets', async () => {
    const update = setUp();

    await checkServing(update, '{"error":"not found"}');

    expect(update.available()).toBe(false);
  });

  it('says nothing when the page cannot be fetched', async () => {
    const update = setUp();

    const done = update.check();
    http.expectOne('/index.html').flush('down', { status: 503, statusText: 'Unavailable' });
    await done;

    expect(update.available()).toBe(false);
  });

  it('checks when the hub reconnects', () => {
    const update = setUp();
    update.start();

    TestBed.inject(FakeActivityHub).reconnected.next();

    http.expectOne('/index.html');
  });

  it('checks when the tab is shown again and not when it is hidden', () => {
    setUp().start();

    stubVisibility('hidden');
    document.dispatchEvent(new Event('visibilitychange'));
    http.expectNone('/index.html');

    stubVisibility('visible');
    document.dispatchEvent(new Event('visibilitychange'));
    http.expectOne('/index.html');
  });

  it('stops checking once an update is known', async () => {
    const update = setUp();
    update.start();
    await checkServing(update, page('main-BBBB.js', 'styles-AAAA.css'));

    TestBed.inject(FakeActivityHub).reconnected.next();
    stubVisibility('visible');
    document.dispatchEvent(new Event('visibilitychange'));

    http.expectNone('/index.html');
  });

  it('checks nothing before it is started', () => {
    setUp();

    TestBed.inject(FakeActivityHub).reconnected.next();

    http.expectNone('/index.html');
  });

  it('stops listening when it is destroyed', () => {
    setUp().start();
    const hub = TestBed.inject(FakeActivityHub);

    TestBed.resetTestingModule();
    stubVisibility('visible');
    document.dispatchEvent(new Event('visibilitychange'));
    hub.reconnected.next();

    http.expectNone('/index.html');
  });

  it('reloads the page', () => {
    const reload = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideFakeActivityHub(),
        { provide: DOCUMENT, useValue: { defaultView: { location: { reload } } } },
      ],
    });

    TestBed.inject(AppUpdate).reload();

    expect(reload).toHaveBeenCalledTimes(1);
  });
});
