import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { components } from '../../api/schema';
import { text } from '../../testing/element-text';
import { WorksListPage } from './works-list-page';

type PageOfWorkSummaryResponse = components['schemas']['PageOfWorkSummaryResponse'];
type WorkSummaryResponse = components['schemas']['WorkSummaryResponse'];

@Component({ template: '<h1>Work</h1>' })
class WorkDetailStub {}

describe('WorksListPage', () => {
  const booksId = '0198a1b2-0000-7000-8000-00000000000a';
  const comicsId = '0198a1b2-0000-7000-8000-00000000000b';

  const dune: WorkSummaryResponse = {
    id: '0198a1b2-0000-7000-8000-000000000001',
    libraryId: booksId,
    title: 'Dune',
    authors: ['Frank Herbert'],
    series: { id: '0198a1b2-0000-7000-8000-0000000000c1', name: 'Dune', position: 1 },
    coverHash: 'abc123',
    formats: ['epub'],
    addedAt: '2026-11-01T12:00:00Z',
  };
  const goodOmens: WorkSummaryResponse = {
    id: '0198a1b2-0000-7000-8000-000000000002',
    libraryId: booksId,
    title: 'Good Omens',
    authors: ['Terry Pratchett', 'Neil Gaiman'],
    series: null,
    coverHash: null,
    formats: ['epub'],
    addedAt: '2026-11-02T12:00:00Z',
  };
  const theMartian: WorkSummaryResponse = {
    id: '0198a1b2-0000-7000-8000-000000000003',
    libraryId: comicsId,
    title: 'The Martian',
    authors: ['Andy Weir'],
    series: { id: '0198a1b2-0000-7000-8000-0000000000c2', name: 'Mars', position: null },
    coverHash: 'def456',
    formats: [],
    addedAt: '2026-11-03T12:00:00Z',
  };

  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let router: Router;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'works', component: WorksListPage },
          { path: 'works/:id', component: WorkDetailStub },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => httpTesting.verify());

  function pageOf(
    items: WorkSummaryResponse[],
    total = items.length,
    offset = 0,
  ): PageOfWorkSummaryResponse {
    return { items, total, offset, limit: 50 };
  }

  function worksRequest(): Promise<TestRequest> {
    return vi.waitFor(() => httpTesting.expectOne((request) => request.url === '/api/works'));
  }

  /** Opens the list at `url` and answers its request for works with `works`, out of `total` in all. */
  async function openList(
    url: string,
    works: WorkSummaryResponse[] = [dune, goodOmens, theMartian],
    total = works.length,
  ): Promise<{ page: HTMLElement; request: TestRequest }> {
    const navigating = harness.navigateByUrl(url);
    const request = await worksRequest();
    request.flush(pageOf(works, total, Number(request.request.params.get('Offset'))));
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await shown(page);
    return { page, request };
  }

  /** Waits for the page to show what the API answered: the grid, "No works yet" or an error. */
  async function shown(page: HTMLElement): Promise<void> {
    await vi.waitFor(() => expect(page.querySelector('ul, p')).not.toBeNull());
  }

  /** The grid's works, one per cell. */
  function gridItems(page: HTMLElement): Element[] {
    return Array.from(page.querySelectorAll('ul[aria-label="Works"] > li'));
  }

  /** What each cell of the grid says beneath its cover: the title, then the authors. */
  function captions(page: HTMLElement): string[][] {
    return gridItems(page).map((item) =>
      Array.from(item.querySelectorAll('app-work-cover ~ *')).map(text),
    );
  }

  /** Answers the request the page makes after a control changes the URL, and waits for the page to show it. */
  async function answerNext(
    page: HTMLElement,
    works: WorkSummaryResponse[],
    total: number,
  ): Promise<TestRequest> {
    const request = await worksRequest();
    request.flush(pageOf(works, total, Number(request.request.params.get('Offset'))));
    await shown(page);
    return request;
  }

  function buttonNamed(page: HTMLElement, name: string): HTMLButtonElement {
    const button = Array.from(page.querySelectorAll('button')).find(
      (candidate) => candidate.textContent?.trim() === name,
    );
    if (!button) {
      throw new Error(`No button named ${name}`);
    }
    return button;
  }

  /** The names of the sort options shown as chosen. */
  function chosenSorts(page: HTMLElement): string[] {
    return Array.from(page.querySelectorAll('[role="group"] button[aria-pressed="true"]')).map(
      (button) => button.textContent?.trim() ?? '',
    );
  }

  it('shows each work as its cover with the title and authors beneath', async () => {
    const { page } = await openList('/works', [dune, theMartian]);

    const items = gridItems(page);
    expect(items.map((item) => item.querySelector('img')?.getAttribute('src'))).toEqual([
      `/api/works/${dune.id}/cover?size=small&v=abc123`,
      `/api/works/${theMartian.id}/cover?size=small&v=def456`,
    ]);
    expect(captions(page)).toEqual([
      ['Dune', 'Frank Herbert'],
      ['The Martian', 'Andy Weir'],
    ]);
    for (const item of items) {
      const cover = item.querySelector('app-work-cover')!;
      expect(cover.contains(item.querySelector('img'))).toBe(true);
      expect(Array.from(cover.parentElement!.children).indexOf(cover)).toBe(0);
    }
    expect(page.textContent).not.toContain('Mars');
    expect(page.textContent).not.toContain('EPUB');
  });

  it('shows no table', async () => {
    const { page } = await openList('/works');

    expect(page.querySelector('table, [role="table"], [role="grid"]')).toBeNull();
  });

  it('shows a placeholder with the title for a work with no cover', async () => {
    const { page } = await openList('/works');

    const item = gridItems(page)[1];
    const cover = item.querySelector('app-work-cover')!;
    expect(cover.querySelector('img')).toBeNull();
    expect(text(cover)).toBe('Good Omens');
    expect(captions(page)[1]).toEqual(['Good Omens', 'Terry Pratchett, Neil Gaiman']);
  });

  it('links each work to its detail page', async () => {
    const { page } = await openList('/works');

    const links = gridItems(page).map((item) => item.querySelector<HTMLAnchorElement>('a')!);
    expect(links.map((link) => link.getAttribute('href'))).toEqual([
      `/works/${dune.id}`,
      `/works/${goodOmens.id}`,
      `/works/${theMartian.id}`,
    ]);
    for (const link of links) {
      expect(link.querySelector('app-work-cover')).not.toBeNull();
    }

    links[1].click();
    await vi.waitFor(() => expect(router.url).toBe(`/works/${goodOmens.id}`));
  });

  it('asks for every library when the URL names none', async () => {
    const { request } = await openList('/works');

    const params = request.request.params;
    expect(params.has('LibraryId')).toBe(false);
    expect(params.get('Sort')).toBe('title');
    expect(params.get('Offset')).toBe('0');
    expect(params.get('Limit')).toBe('50');
  });

  it('asks for one library when the URL names it', async () => {
    const { request } = await openList(`/works?library=${booksId}`, [dune, goodOmens]);

    expect(request.request.params.get('LibraryId')).toBe(booksId);
  });

  it('asks again when the URL switches library while the page is open', async () => {
    const { page } = await openList('/works');
    const listPage = harness.routeDebugElement?.componentInstance;

    const navigating = harness.navigateByUrl(`/works?library=${comicsId}`);
    const request = await worksRequest();
    expect(request.request.params.get('LibraryId')).toBe(comicsId);
    request.flush(pageOf([theMartian]));
    await navigating;

    await vi.waitFor(() =>
      expect(captions(page).map((caption) => caption[0])).toEqual(['The Martian']),
    );
    expect(harness.routeDebugElement?.componentInstance).toBe(listPage);
  });

  it('asks for 50 works sorted by title when the URL has no parameters', async () => {
    const { page, request } = await openList('/works');

    const params = request.request.params;
    expect(params.get('Sort')).toBe('title');
    expect(params.get('Offset')).toBe('0');
    expect(params.get('Limit')).toBe('50');
    expect(chosenSorts(page)).toEqual(['Title']);
    expect(buttonNamed(page, 'Recently added')).toBeTruthy();
  });

  it('loads the library, sort and page the URL names', async () => {
    const { page, request } = await openList(
      `/works?library=${booksId}&sort=added&page=3`,
      [dune],
      101,
    );

    const params = request.request.params;
    expect(params.get('LibraryId')).toBe(booksId);
    expect(params.get('Sort')).toBe('added');
    expect(params.get('Offset')).toBe('100');
    expect(params.get('Limit')).toBe('50');
    expect(chosenSorts(page)).toEqual(['Recently added']);
    expect(page.textContent).toContain('Page 3 of 3');
  });

  it('choosing Recently added puts sort=added in the URL and goes back to page 1', async () => {
    const { page } = await openList(`/works?library=${booksId}&page=2`, [dune], 120);

    buttonNamed(page, 'Recently added').click();
    const request = await answerNext(page, [goodOmens], 120);

    expect(router.url).toBe(`/works?library=${booksId}&sort=added`);
    const params = request.request.params;
    expect(params.get('LibraryId')).toBe(booksId);
    expect(params.get('Sort')).toBe('added');
    expect(params.get('Offset')).toBe('0');
    expect(chosenSorts(page)).toEqual(['Recently added']);
    expect(page.textContent).toContain('Page 1 of 3');
  });

  it('choosing the sort that is already chosen keeps it chosen', async () => {
    const { page } = await openList('/works?sort=added');

    buttonNamed(page, 'Recently added').click();
    harness.detectChanges();

    expect(chosenSorts(page)).toEqual(['Recently added']);
    expect(router.url).toBe('/works?sort=added');
  });

  it('choosing Title leaves sort out of the URL', async () => {
    const { page } = await openList('/works?sort=added&page=2', [dune], 120);

    buttonNamed(page, 'Title').click();
    const request = await answerNext(page, [dune], 120);

    expect(router.url).toBe('/works');
    expect(request.request.params.get('Sort')).toBe('title');
    expect(chosenSorts(page)).toEqual(['Title']);
  });

  it('Next puts page=2 in the URL and asks for offset 50', async () => {
    const { page } = await openList('/works', [dune, goodOmens, theMartian], 120);

    buttonNamed(page, 'Next').click();
    const request = await answerNext(page, [theMartian], 120);

    expect(router.url).toBe('/works?page=2');
    expect(request.request.params.get('Sort')).toBe('title');
    expect(request.request.params.get('Offset')).toBe('50');
    expect(captions(page).map((caption) => caption[0])).toEqual(['The Martian']);
    expect(page.textContent).toContain('Page 2 of 3');
  });

  it('Previous from page 2 leaves page out of the URL and keeps the library and sort', async () => {
    const { page } = await openList(`/works?library=${booksId}&sort=added&page=2`, [dune], 120);

    buttonNamed(page, 'Previous').click();
    const request = await answerNext(page, [goodOmens], 120);

    expect(router.url).toBe(`/works?library=${booksId}&sort=added`);
    expect(request.request.params.get('Offset')).toBe('0');
  });

  it('Previous is disabled on the first page and Next on the last', async () => {
    const { page } = await openList('/works', [dune], 120);
    expect(buttonNamed(page, 'Previous').disabled).toBe(true);
    expect(buttonNamed(page, 'Next').disabled).toBe(false);

    await openList('/works?page=2', [dune], 120);
    expect(buttonNamed(page, 'Previous').disabled).toBe(false);
    expect(buttonNamed(page, 'Next').disabled).toBe(false);

    await openList('/works?page=3', [dune], 120);
    expect(buttonNamed(page, 'Previous').disabled).toBe(false);
    expect(buttonNamed(page, 'Next').disabled).toBe(true);
  });

  it('disables both Previous and Next when every work fits on one page', async () => {
    const { page } = await openList('/works', [dune, goodOmens], 2);

    expect(buttonNamed(page, 'Previous').disabled).toBe(true);
    expect(buttonNamed(page, 'Next').disabled).toBe(true);
  });

  it('shows the total', async () => {
    const { page } = await openList('/works', [dune], 120);
    expect(page.textContent).toContain('120 works');

    await openList(`/works?library=${booksId}`, [dune], 1);
    expect(page.textContent).toContain('1 work');
    expect(page.textContent).not.toContain('1 works');
  });

  it('has no page-size control', async () => {
    const { page } = await openList('/works', [dune], 120);

    expect(
      page.querySelectorAll('input, select, [role="combobox"], [role="listbox"]'),
    ).toHaveLength(0);
    expect(
      Array.from(page.querySelectorAll('button')).map((button) => button.textContent?.trim()),
    ).toEqual(['Title', 'Recently added', 'Previous', 'Next']);
    expect(page.textContent).not.toMatch(/per page|page size/i);
  });

  it.each([
    '/works?sort=newest&page=0',
    '/works?sort=TITLE&page=abc',
    '/works?sort=&page=-2',
    '/works?page=1.5',
  ])('treats an unknown sort or page as the default: %s', async (url) => {
    const { page, request } = await openList(url, [dune], 120);

    expect(request.request.params.get('Sort')).toBe('title');
    expect(request.request.params.get('Offset')).toBe('0');
    expect(chosenSorts(page)).toEqual(['Title']);
    expect(page.textContent).toContain('Page 1 of 3');
  });

  it('shows No works yet in place of the grid when there are none', async () => {
    const { page } = await openList('/works', []);

    expect(page.querySelector('ul')).toBeNull();
    expect(page.textContent).toContain('No works yet');
  });

  it('says something went wrong when the works cannot be loaded', async () => {
    const navigating = harness.navigateByUrl('/works');
    (await worksRequest()).flush(null, { status: 500, statusText: 'Internal Server Error' });
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await shown(page);

    expect(page.querySelector('ul')).toBeNull();
    expect(page.querySelector('[role="alert"]')?.textContent?.trim()).toBe(
      'Something went wrong. Try again.',
    );
  });
});
