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
import { PageName } from '../../core/page-name';
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
    access: 'download',
    addedBy: null,
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
      Array.from(item.querySelectorAll('a, span')).map(text),
    );
  }

  /** The Files section, which lists every item and part. */
  function filesSection(page: HTMLElement): Element | null {
    return page.querySelector('section[aria-labelledby="files-heading"]');
  }

  /** Each item's format, then one "media type, size" pair per part. */
  function items(page: HTMLElement): { format: string; parts: string[][] }[] {
    return Array.from(filesSection(page)?.querySelector('ul')?.children ?? []).map((item) => ({
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

  it('links each contributor to their page whatever their role', async () => {
    const page = await openWork(dune);

    const links = Array.from(page.querySelectorAll('[aria-label="Contributors"] li a'));
    expect(links.map((link) => [text(link), link.getAttribute('href')])).toEqual([
      ['Frank Herbert', '/people/0198a1b2-0000-7000-8000-0000000000d1'],
      ['Scott Brick', '/people/0198a1b2-0000-7000-8000-0000000000d2'],
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

  /** The links in the Files section as label and address pairs. */
  function fileLinks(page: HTMLElement): [string, string | null][] {
    return Array.from(filesSection(page)?.querySelectorAll('a') ?? []).map((link) => [
      text(link),
      link.getAttribute('href'),
    ]);
  }

  /** The buttons beside the cover as label and address pairs. One that is not a link has no address. */
  function actions(page: HTMLElement): [string, string | null][] {
    return Array.from(page.querySelectorAll('[aria-label="Actions"] :is(a, button)')).map(
      (action) => [text(action), action.getAttribute('href')],
    );
  }

  const epub = dune.items[1];
  const audiobook = {
    id: '0198a1b2-0000-7000-8000-0000000000f6',
    format: 'm4b' as ItemFormat,
    parts: [{ id: '0198a1b2-0000-7000-8000-0000000000f7', mediaType: 'audio/mp4', size: 900 }],
  };

  it('offers beside the cover to read the first item it can read', async () => {
    const page = await openWork({ ...dune, access: 'read' });

    expect(actions(page)).toEqual([
      ['Read', `/works/${duneId}/read/0198a1b2-0000-7000-8000-0000000000f4`],
    ]);
  });

  it('offers beside the cover to download each format when each is one file', async () => {
    const page = await openWork({ ...dune, items: [epub, audiobook] });

    expect(actions(page)).toEqual([
      ['Read', `/works/${duneId}/read/${epub.id}`],
      ['Download EPUB', '/api/parts/0198a1b2-0000-7000-8000-0000000000f5/file'],
      ['Download m4b', '/api/parts/0198a1b2-0000-7000-8000-0000000000f7/file'],
    ]);
    for (const link of page.querySelectorAll('[aria-label="Actions"] a[href^="/api/"]')) {
      expect(link.hasAttribute('download')).toBe(true);
    }
  });

  it.each([
    ['an item has several parts', [dune.items[0]]],
    ['two items share a format', [epub, { ...audiobook, format: 'epub' as ItemFormat }]],
  ])('goes to the Files section from the Download beside the cover when %s', async (_, items) => {
    // jsdom has no scrollIntoView.
    const scrollIntoView = vi.fn();
    Element.prototype.scrollIntoView = scrollIntoView;
    try {
      const page = await openWork({ ...dune, items });
      const download = Array.from(
        page.querySelectorAll<HTMLButtonElement>('[aria-label="Actions"] button'),
      ).find((button) => text(button) === 'Download');

      download!.click();

      expect(scrollIntoView.mock.contexts).toEqual([filesSection(page)]);
    } finally {
      delete (Element.prototype as Partial<Element>).scrollIntoView;
    }
  });

  it('lists the files among the details when the buttons beside the cover reach each one', async () => {
    const page = await openWork({ ...dune, items: [epub, audiobook] });

    expect(filesSection(page)).toBeNull();
    expect(details(page).at(-1)?.[0]).toBe('Files');
    expect(
      Array.from(detail(page, 'Files').querySelectorAll('li')).map((item) =>
        Array.from(item.querySelectorAll('span')).map(text),
      ),
    ).toEqual([
      ['EPUB', 'application/epub+zip', '900 B'],
      ['m4b', 'audio/mp4', '900 B'],
    ]);
    expect(detail(page, 'Files').querySelector('a')).toBeNull();
  });

  it('shows the files as its only detail for a work with nothing else', async () => {
    const page = await openWork({ ...bare, items: [epub] });

    expect(details(page).map(([label]) => label)).toEqual(['Files']);
  });

  it('has a Read for each item in the Files section when several can be read', async () => {
    const second = { ...audiobook, format: 'epub' as ItemFormat };
    const page = await openWork({ ...dune, access: 'read', items: [epub, second] });

    expect(actions(page)).toEqual([['Read', `/works/${duneId}/read/${epub.id}`]]);
    expect(fileLinks(page)).toEqual([
      ['Read', `/works/${duneId}/read/${epub.id}`],
      ['Read', `/works/${duneId}/read/${second.id}`],
    ]);
  });

  it('has no buttons beside the cover at the browse level', async () => {
    const page = await openWork({ ...dune, access: 'browse' });

    expect(page.querySelector('[aria-label="Actions"]')).toBeNull();
  });

  it('has no buttons beside the cover for a work whose items have no parts', async () => {
    const page = await openWork({ ...dune, items: [{ ...epub, parts: [] }] });

    expect(page.querySelector('[aria-label="Actions"]')).toBeNull();
  });

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

    expect(text(detail(page, 'Files'))).toContain('m4b');
    expect(readLinks(page)).toEqual([]);
  });

  /** The "Download" links in the Files section. */
  function downloadLinks(page: HTMLElement): HTMLAnchorElement[] {
    return Array.from(filesSection(page)?.querySelectorAll('a') ?? []).filter(
      (link) => text(link) === 'Download',
    );
  }

  it('shows neither Read nor Download at the browse level', async () => {
    const page = await openWork({ ...dune, access: 'browse' });

    expect(items(page)).toHaveLength(2);
    expect(readLinks(page)).toEqual([]);
    expect(downloadLinks(page)).toEqual([]);
  });

  it('shows Read and no Download at the read level', async () => {
    const page = await openWork({ ...dune, access: 'read' });

    expect(readLinks(page).map((link) => link.getAttribute('href'))).toEqual([
      `/works/${duneId}/read/0198a1b2-0000-7000-8000-0000000000f4`,
    ]);
    expect(downloadLinks(page)).toEqual([]);
  });

  it('shows Read and Download at the download level', async () => {
    const page = await openWork({ ...dune, access: 'download' });

    expect(readLinks(page)).toHaveLength(1);
    expect(downloadLinks(page)).toHaveLength(3);
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
    expect(page.querySelector('[aria-label="Actions"]')).toBeNull();
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

  it('says who added the work when the response names them', async () => {
    const page = await openWork({ ...bare, addedBy: 'Ada Adder' });

    expect(details(page)).toEqual([['Added by', 'Ada Adder on November 1, 2026']]);
  });

  it('says nothing about who added it otherwise', async () => {
    // The API leaves the field out when there is no one to name.
    const unnamed: Partial<WorkDetailResponse> = { ...dune };
    delete unnamed.addedBy;
    const page = await openWork(unnamed as WorkDetailResponse);

    expect(details(page).map(([label]) => label)).not.toContain('Added by');
    expect(text(page)).not.toContain('Added by');
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

  it('tells the header the title of the work it shows', async () => {
    await openWork(dune);

    expect(TestBed.inject(PageName).of(dune.id)).toBe('Dune');
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
    expect(TestBed.inject(PageName).of('missing')).toBe('Work not found');
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
