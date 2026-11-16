import { Location } from '@angular/common';
import { provideLocationMocks } from '@angular/common/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, RouterLink, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { components } from '../../api/schema';
import { text } from '../../testing/element-text';
import { WorkDetailPage } from './work-detail-page';

type ContributorRole = components['schemas']['ContributorRole'];
type ItemFormat = components['schemas']['ItemFormat'];
type WorkDetailResponse = components['schemas']['WorkDetailResponse'];

const duneId = '0198a1b2-0000-7000-8000-000000000001';

@Component({
  imports: [RouterLink],
  template: `<h1>Works</h1>
    <a [routerLink]="['/works', workId]">Dune</a>`,
})
class WorksListStub {
  protected readonly workId = duneId;
}

describe('WorkDetailPage', () => {
  const dune: WorkDetailResponse = {
    id: duneId,
    sectionId: '0198a1b2-0000-7000-8000-00000000000a',
    title: 'Dune',
    sortTitle: 'Dune',
    description: 'A desert planet.\nA spice.',
    language: 'en',
    publicationDate: '1965-08-01',
    publisher: 'Chilton Books',
    series: { id: '0198a1b2-0000-7000-8000-0000000000c1', name: 'Dune', position: 1 },
    coverHash: 'abc123',
    contributors: [
      { personId: '0198a1b2-0000-7000-8000-0000000000d1', name: 'Frank Herbert', role: 'author' },
      { personId: '0198a1b2-0000-7000-8000-0000000000d2', name: 'Scott Brick', role: 'narrator' },
    ],
    tags: [
      { id: '0198a1b2-0000-7000-8000-0000000000e1', name: 'Science fiction' },
      { id: '0198a1b2-0000-7000-8000-0000000000e2', name: 'Classics' },
    ],
    externalIds: [
      { scheme: 'isbn', value: '9780441013593' },
      { scheme: 'goodreads', value: '44767458' },
    ],
    items: [
      {
        id: '0198a1b2-0000-7000-8000-0000000000f1',
        format: 'epub',
        parts: [
          {
            id: '0198a1b2-0000-7000-8000-0000000000f2',
            mediaType: 'application/epub+zip',
            size: 1_468_006,
          },
          { id: '0198a1b2-0000-7000-8000-0000000000f3', mediaType: 'image/jpeg', size: 524_288 },
        ],
      },
      {
        id: '0198a1b2-0000-7000-8000-0000000000f4',
        format: 'epub',
        parts: [
          {
            id: '0198a1b2-0000-7000-8000-0000000000f5',
            mediaType: 'application/epub+zip',
            size: 900,
          },
        ],
      },
    ],
    addedAt: '2026-11-01T12:00:00Z',
    updatedAt: '2026-11-02T12:00:00Z',
  };

  const bare: WorkDetailResponse = {
    ...dune,
    description: null,
    language: null,
    publicationDate: null,
    publisher: null,
    series: null,
    contributors: [],
    tags: [],
    externalIds: [],
    items: [],
  };

  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let router: Router;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'library', component: WorksListStub },
          { path: 'works/:id', component: WorkDetailPage },
        ]),
        provideLocationMocks(),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => httpTesting.verify());

  function workRequest(id: string): Promise<TestRequest> {
    return vi.waitFor(() => httpTesting.expectOne(`/api/works/${id}`));
  }

  /** Waits for the page to show what the API answered: the work, "Work not found" or an error. */
  async function shown(page: HTMLElement): Promise<void> {
    await vi.waitFor(() => expect(page.querySelector('h1, [role="alert"]')).not.toBeNull());
  }

  /** Opens the detail page for `work` and answers its request with it. */
  async function openWork(work: WorkDetailResponse): Promise<HTMLElement> {
    const navigating = harness.navigateByUrl(`/works/${work.id}`);
    (await workRequest(work.id)).flush(work);
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await shown(page);
    return page;
  }

  /** The page's details as label and text pairs, in the order shown. */
  function details(page: HTMLElement): [string, string][] {
    return Array.from(page.querySelectorAll('dl dt')).map((term) => [
      text(term),
      text(term.nextElementSibling),
    ]);
  }

  function detail(page: HTMLElement, label: string): Element {
    const term = Array.from(page.querySelectorAll('dl dt')).find(
      (candidate) => text(candidate) === label,
    );
    if (!term?.nextElementSibling) {
      throw new Error(`No detail labelled ${label}`);
    }
    return term.nextElementSibling;
  }

  function contributors(page: HTMLElement): string[][] {
    return Array.from(page.querySelectorAll('[aria-label="Contributors"] li')).map((item) =>
      Array.from(item.querySelectorAll('span')).map(text),
    );
  }

  /** Each item's format, then one "media type, size" pair per part. */
  function items(page: HTMLElement): { format: string; parts: string[][] }[] {
    const files = Array.from(page.querySelectorAll('h2')).find(
      (heading) => text(heading) === 'Files',
    );
    return Array.from(files?.nextElementSibling?.children ?? []).map((item) => ({
      format: text(item.querySelector('[data-slot="badge"]')),
      parts: Array.from(item.querySelectorAll('li')).map((part) =>
        Array.from(part.querySelectorAll('span')).map(text),
      ),
    }));
  }

  it('shows the title', async () => {
    const page = await openWork(dune);

    expect(text(page.querySelector('h1'))).toBe('Dune');
  });

  it('shows contributors with their roles', async () => {
    const page = await openWork(dune);

    expect(contributors(page)).toEqual([
      ['Frank Herbert', 'Author'],
      ['Scott Brick', 'Narrator'],
    ]);
  });

  it('shows a role it has no label for as the API sent it', async () => {
    const page = await openWork({
      ...dune,
      contributors: [{ ...dune.contributors[0], role: 'illustrator' as ContributorRole }],
    });

    expect(contributors(page)).toEqual([['Frank Herbert', 'illustrator']]);
  });

  it.each(['Asia/Tokyo', 'America/Los_Angeles'])(
    'shows the publication date as the API sent it in the %s time zone',
    async (timeZone) => {
      vi.stubEnv('TZ', timeZone);
      try {
        const page = await openWork(dune);

        expect(text(detail(page, 'Published'))).toBe('August 1, 1965');
      } finally {
        vi.unstubAllEnvs();
      }
    },
  );

  it('shows series and position, description, publisher, publication date and language', async () => {
    const page = await openWork(dune);

    expect(text(detail(page, 'Series'))).toBe('Dune #1');
    expect(text(detail(page, 'Publisher'))).toBe('Chilton Books');
    expect(text(detail(page, 'Published'))).toBe('August 1, 1965');
    expect(text(detail(page, 'Language'))).toBe('en');
    expect(page.querySelector('[aria-label="Description"] p')?.textContent).toBe(
      'A desert planet.\nA spice.',
    );
  });

  it('shows a series without a position by its name', async () => {
    const page = await openWork({ ...dune, series: { ...dune.series!, position: null } });

    expect(text(detail(page, 'Series'))).toBe('Dune');
  });

  it('shows tags and external ids', async () => {
    const page = await openWork(dune);

    expect(
      Array.from(detail(page, 'Tags').querySelectorAll('[data-slot="badge"]')).map(text),
    ).toEqual(['Science fiction', 'Classics']);
    expect(
      Array.from(detail(page, 'Identifiers').querySelectorAll('li')).map((item) =>
        Array.from(item.querySelectorAll('span')).map(text),
      ),
    ).toEqual([
      ['isbn', '9780441013593'],
      ['goodreads', '44767458'],
    ]);
  });

  it("shows each item's format with its parts' media types and sizes", async () => {
    const page = await openWork(dune);

    expect(items(page)).toEqual([
      {
        format: 'EPUB',
        parts: [
          ['application/epub+zip', '1.4 MB'],
          ['image/jpeg', '512.0 KB'],
        ],
      },
      { format: 'EPUB', parts: [['application/epub+zip', '900 B']] },
    ]);
  });

  it('has a download link for each part', async () => {
    const page = await openWork({
      ...dune,
      items: [{ ...dune.items[0], parts: [dune.items[0].parts[0]] }, dune.items[1]],
    });

    const links = Array.from(page.querySelectorAll('a')).filter(
      (link) => text(link) === 'Download',
    );
    expect(links.map((link) => link.getAttribute('href'))).toEqual([
      '/api/parts/0198a1b2-0000-7000-8000-0000000000f2/file',
      '/api/parts/0198a1b2-0000-7000-8000-0000000000f5/file',
    ]);
    for (const link of links) {
      expect(link.hasAttribute('download')).toBe(true);
    }
  });

  /** The page's "Read" links. */
  function readLinks(page: HTMLElement): HTMLAnchorElement[] {
    return Array.from(page.querySelectorAll('a')).filter((link) => text(link) === 'Read');
  }

  it('offers to read an EPUB item that has one part', async () => {
    const page = await openWork(dune);

    expect(readLinks(page).map((link) => link.getAttribute('href'))).toEqual([
      `/works/${duneId}/read/0198a1b2-0000-7000-8000-0000000000f4`,
    ]);
  });

  it('offers no reading for an item with no part or several', async () => {
    const page = await openWork({
      ...dune,
      items: [dune.items[0], { ...dune.items[1], parts: [] }],
    });

    expect(items(page)).toHaveLength(2);
    expect(readLinks(page)).toEqual([]);
  });

  it('offers no reading for an item in a format it cannot read', async () => {
    const page = await openWork({
      ...dune,
      items: [{ ...dune.items[1], format: 'm4b' as ItemFormat }],
    });

    expect(items(page)).toHaveLength(1);
    expect(readLinks(page)).toEqual([]);
  });

  it('shows a format it has no label for as the API sent it', async () => {
    const page = await openWork({
      ...dune,
      items: [{ ...dune.items[0], format: 'm4b' as ItemFormat }],
    });

    expect(items(page).map((item) => item.format)).toEqual(['m4b']);
  });

  it('leaves out fields the work does not have', async () => {
    const page = await openWork(bare);

    expect(text(page.querySelector('h1'))).toBe('Dune');
    expect(details(page)).toEqual([]);
    expect(page.querySelector('dl')).toBeNull();
    expect(page.querySelector('[aria-label="Contributors"]')).toBeNull();
    expect(page.querySelector('[aria-label="Description"]')).toBeNull();
    expect(page.querySelector('h2')).toBeNull();
  });

  it('shows every detail it has in order', async () => {
    const page = await openWork(dune);

    expect(details(page).map(([label]) => label)).toEqual([
      'Series',
      'Publisher',
      'Published',
      'Language',
      'Tags',
      'Identifiers',
    ]);
  });

  it('shows the cover at the large size', async () => {
    const page = await openWork(dune);

    const images = Array.from(page.querySelectorAll('img'));
    expect(images.map((image) => image.getAttribute('src'))).toEqual([
      `/api/works/${duneId}/cover?size=large&v=abc123`,
    ]);
  });

  it('shows a placeholder with the title when the work has no cover', async () => {
    const page = await openWork({ ...dune, coverHash: null });

    const cover = page.querySelector('app-work-cover');
    expect(page.querySelector('img')).toBeNull();
    expect(text(cover)).toBe('Dune');
  });

  it('shows Work not found when the API answers 404', async () => {
    const navigating = harness.navigateByUrl('/works/missing');
    (await workRequest('missing')).flush(
      { title: 'Not Found', status: 404 },
      { status: 404, statusText: 'Not Found' },
    );
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await shown(page);

    expect(text(page.querySelector('h1'))).toBe('Work not found');
    expect(page.querySelector('dl')).toBeNull();
  });

  it('says something went wrong when the work cannot be loaded', async () => {
    const navigating = harness.navigateByUrl(`/works/${dune.id}`);
    (await workRequest(dune.id)).flush(null, {
      status: 500,
      statusText: 'Internal Server Error',
    });
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await shown(page);

    expect(page.querySelector('h1')).toBeNull();
    expect(text(page.querySelector('[role="alert"]'))).toBe('Something went wrong. Try again.');
  });

  it('shows the next work when the URL switches to it while the page is open', async () => {
    const page = await openWork(dune);
    const detailPage = harness.routeDebugElement?.componentInstance;

    const otherId = '0198a1b2-0000-7000-8000-000000000002';
    const navigating = harness.navigateByUrl(`/works/${otherId}`);
    (await workRequest(otherId)).flush({ ...bare, id: otherId, title: 'Good Omens' });
    await navigating;

    await vi.waitFor(() => expect(text(page.querySelector('h1'))).toBe('Good Omens'));
    expect(harness.routeDebugElement?.componentInstance).toBe(detailPage);
  });

  it('going back returns to the list URL it was opened from', async () => {
    // A running app does this on its first navigation, which a test never makes.
    router.setUpLocationChangeListener();
    const listUrl = '/library?section=0198a1b2-0000-7000-8000-00000000000a&sort=added&page=2';
    await harness.navigateByUrl(listUrl);
    const link = (harness.routeNativeElement as HTMLElement).querySelector('a')!;

    link.click();
    (await workRequest(dune.id)).flush(dune);
    await vi.waitFor(() => expect(router.url).toBe(`/works/${dune.id}`));
    await shown(harness.routeNativeElement as HTMLElement);

    TestBed.inject(Location).back();

    await vi.waitFor(() => expect(router.url).toBe(listUrl));
    expect(text((harness.routeNativeElement as HTMLElement).querySelector('h1'))).toBe('Works');
  });
});
