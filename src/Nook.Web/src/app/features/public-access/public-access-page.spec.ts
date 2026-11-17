import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { components } from '../../api/schema';
import { buttonLabelled } from '../../testing/button-labelled';
import { text } from '../../testing/element-text';
import { fieldLabelled } from '../../testing/field-labelled';
import { PublicAccessPage } from './public-access-page';

type PublicAccessRequest = components['schemas']['PublicAccessRequest'];
type PublicAccessResponse = components['schemas']['PublicAccessResponse'];

describe('PublicAccessPage', () => {
  const books = '0198a1b2-0000-7000-8000-00000000000a';
  const comics = '0198a1b2-0000-7000-8000-00000000000b';
  const saved: PublicAccessResponse = {
    enabled: true,
    access: 'read',
    defaultAreaAccess: 'browse',
    sections: [
      { id: books, name: 'Books', access: 'read' },
      { id: comics, name: 'Comics', access: 'none' },
    ],
  };

  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'public-access', component: PublicAccessPage }]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => httpTesting.verify());

  /** Opens the page and answers its request for the settings with `settings`, or with a failure when it is null. */
  async function openPage(settings: PublicAccessResponse | null): Promise<HTMLElement> {
    const navigating = harness.navigateByUrl('/public-access');
    const request = await vi.waitFor(() => httpTesting.expectOne('/api/public-access'));
    if (settings) {
      request.flush(settings);
    } else {
      request.flush(null, { status: 500, statusText: 'Internal Server Error' });
    }
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(page.querySelector('form, [role="alert"]')).not.toBeNull();
    });
    return page;
  }

  /** The level each list labelled in `labels` shows as chosen, by its label. */
  function chosenLevels(page: HTMLElement, labels: string[]): Record<string, string> {
    return Object.fromEntries(
      labels.map((label) => [
        label,
        text(fieldLabelled(page, label, HTMLSelectElement).selectedOptions[0]),
      ]),
    );
  }

  function choose(page: HTMLElement, label: string, level: string): void {
    const list = fieldLabelled(page, label, HTMLSelectElement);
    const option = Array.from(list.options).find((candidate) => text(candidate) === level);
    if (!option) {
      throw new Error(`${label} offers no ${level}`);
    }
    list.value = option.value;
    list.dispatchEvent(new Event('change'));
  }

  async function save(page: HTMLElement): Promise<ReturnType<HttpTestingController['expectOne']>> {
    buttonLabelled(page, 'Save').click();
    return vi.waitFor(() => httpTesting.expectOne({ method: 'PUT', url: '/api/public-access' }));
  }

  it('shows the saved settings', async () => {
    const page = await openPage(saved);

    expect(text(page.querySelector('h1'))).toBe('Public access');
    expect(fieldLabelled(page, 'Let visitors with no account in').checked).toBe(true);
    expect(chosenLevels(page, ['Anonymous', 'Default', 'Books', 'Comics'])).toEqual({
      Anonymous: 'Read',
      Default: 'Browse',
      Books: 'Read',
      Comics: 'None',
    });
    expect(
      Array.from(fieldLabelled(page, 'Anonymous', HTMLSelectElement).options, (option) =>
        text(option),
      ),
    ).toEqual(['None', 'Browse', 'Read', 'Download']);
  });

  it('shows public access off when it is off', async () => {
    const page = await openPage({ ...saved, enabled: false });

    expect(fieldLabelled(page, 'Let visitors with no account in').checked).toBe(false);
  });

  it('says that no level goes above the Anonymous level and that Read hands over the whole book', async () => {
    const page = await openPage(saved);

    expect(text(page)).toContain('No level can go above the Anonymous level');
    expect(text(page)).toContain("Read lets a visitor's browser fetch the whole book");
  });

  it('saves what the admin chose', async () => {
    const page = await openPage(saved);

    fieldLabelled(page, 'Let visitors with no account in').click();
    choose(page, 'Anonymous', 'Download');
    choose(page, 'Default', 'None');
    choose(page, 'Comics', 'Download');
    const request = await save(page);

    expect(request.request.body).toEqual({
      enabled: false,
      access: 'download',
      defaultAreaAccess: 'none',
      sections: [
        { id: books, access: 'read' },
        { id: comics, access: 'download' },
      ],
    } satisfies PublicAccessRequest);
    request.flush(null, { status: 204, statusText: 'No Content' });
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(text(page.querySelector('[role="status"]'))).toBe('Saved.');
    });
    expect(page.querySelector('[role="alert"]')).toBeNull();
  });

  it('keeps a section level above the Anonymous level as chosen', async () => {
    const page = await openPage({ ...saved, access: 'browse' });

    choose(page, 'Books', 'Download');
    const request = await save(page);

    expect((request.request.body as PublicAccessRequest).sections).toEqual([
      { id: books, access: 'download' },
      { id: comics, access: 'none' },
    ]);
    request.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('stops saying it saved once the admin changes something', async () => {
    const page = await openPage(saved);
    const request = await save(page);
    request.flush(null, { status: 204, statusText: 'No Content' });
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(page.querySelector('[role="status"]')).not.toBeNull();
    });

    choose(page, 'Comics', 'Read');
    harness.detectChanges();

    expect(page.querySelector('[role="status"]')).toBeNull();
  });

  it('does not say it saved a change made while the save was on its way', async () => {
    const page = await openPage(saved);
    const request = await save(page);

    choose(page, 'Comics', 'Read');
    request.flush(null, { status: 204, statusText: 'No Content' });

    await vi.waitFor(() => {
      harness.detectChanges();
      expect(buttonLabelled(page, 'Save').disabled).toBe(false);
    });
    expect(page.querySelector('[role="status"]')).toBeNull();
  });

  it('says when saving failed', async () => {
    const page = await openPage(saved);

    const request = await save(page);
    request.flush(null, { status: 500, statusText: 'Internal Server Error' });

    await vi.waitFor(() => {
      harness.detectChanges();
      expect(text(page.querySelector('[role="alert"]'))).toBe('Something went wrong. Try again.');
    });
    expect(page.querySelector('[role="status"]')).toBeNull();
    expect(buttonLabelled(page, 'Save').disabled).toBe(false);
  });

  it('says when the settings cannot be loaded, and offers no form', async () => {
    const page = await openPage(null);

    expect(text(page.querySelector('[role="alert"]'))).toBe('Something went wrong. Try again.');
    expect(page.querySelector('form')).toBeNull();
  });
});
