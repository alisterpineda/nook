import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { components } from '../../api/schema';
import { Sections } from '../../core/sections';
import { Session } from '../../core/session';
import { fieldLabelled } from '../../testing/field-labelled';
import { pressEnter } from '../../testing/press-enter';
import { typeInto } from '../../testing/type-into';
import { buttonLabelled } from '../../testing/button-labelled';
import { text } from '../../testing/element-text';
import { linkLabelled } from '../../testing/link-labelled';
import { AddBooks } from '../upload/add-books';
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
    sectionId: booksId,
    title: 'Dune',
    authors: ['Frank Herbert'],
    series: { id: '0198a1b2-0000-7000-8000-0000000000c1', name: 'Dune', position: 1 },
    coverHash: 'abc123',
    formats: ['epub'],
    addedAt: '2026-11-01T12:00:00Z',
  };
  const goodOmens: WorkSummaryResponse = {
    id: '0198a1b2-0000-7000-8000-000000000002',
    sectionId: booksId,
    title: 'Good Omens',
    authors: ['Terry Pratchett', 'Neil Gaiman'],
    series: null,
    coverHash: null,
    formats: ['epub'],
    addedAt: '2026-11-02T12:00:00Z',
  };
  const theMartian: WorkSummaryResponse = {
    id: '0198a1b2-0000-7000-8000-000000000003',
    sectionId: comicsId,
    title: 'The Martian',
    authors: ['Andy Weir'],
    series: { id: '0198a1b2-0000-7000-8000-0000000000c2', name: 'Mars', position: null },
    coverHash: 'def456',
    formats: [],
    addedAt: '2026-11-03T12:00:00Z',
  };
  const inDefault: WorkSummaryResponse = {
    id: '0198a1b2-0000-7000-8000-000000000004',
    sectionId: null,
    title: 'Project Hail Mary',
    authors: ['Andy Weir'],
    series: null,
    coverHash: null,
    formats: ['epub'],
    addedAt: '2026-11-04T12:00:00Z',
  };

  const canUpload = signal(true);
  const openAddBooks = vi.fn<(files: readonly File[], sectionParam: string | null) => void>();

  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let router: Router;

  beforeEach(async () => {
    canUpload.set(true);
    openAddBooks.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'library', component: WorksListPage },
          { path: 'works/:id', component: WorkDetailStub },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Session, useValue: { canUpload } },
        { provide: AddBooks, useValue: { open: openAddBooks } },
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
    const { page } = await openList('/library', [dune, theMartian]);

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

  it('shows a search box labelled Search the library', async () => {
    const { page } = await openList('/library');

    expect(fieldLabelled(page, 'Search the library').type).toBe('search');

    const inSection = await openList(`/library?section=${comicsId}`);

    expect(fieldLabelled(inSection.page, 'Search the library').type).toBe('search');
  });

  it('the search box shows the term the URL names', async () => {
    const { page } = await openList('/library?q=dune', [dune]);

    expect(fieldLabelled(page, 'Search the library').value).toBe('dune');
  });

  it('pressing Enter in the search box asks for the works that match, across the whole library', async () => {
    const { page } = await openList(`/library?section=${comicsId}&sort=added&page=2`, [dune], 120);
    const box = fieldLabelled(page, 'Search the library');

    typeInto(box, 'hail');
    pressEnter(box);
    const request = await answerNext(page, [inDefault], 1);

    expect(request.request.params.get('Search')).toBe('hail');
    expect(request.request.params.has('SectionId')).toBe(false);
    expect(request.request.params.get('Offset')).toBe('0');
  });

  it('the heading reads Library', async () => {
    const { page } = await openList('/library');

    expect(text(page.querySelector('h1'))).toBe('Library');
  });

  it('the heading names the words searched for', async () => {
    const { page } = await openList('/library?q=%20dune%20', [dune]);

    expect(text(page.querySelector('h1'))).toBe('Results for "dune"');
  });

  it('the heading reads Default in the Default area', async () => {
    const { page } = await openList('/library?section=default');

    expect(text(page.querySelector('h1'))).toBe('Default');
  });

  it('the heading names the section once the sections are known', async () => {
    const id = '0198a1b2-0000-7000-8000-00000000000a';
    const { page } = await openList(`/library?section=${id}`);
    expect(text(page.querySelector('h1'))).toBe('Library');

    const loading = TestBed.inject(Sections).load();
    httpTesting.expectOne('/api/sections').flush([{ id, name: 'Books' }]);
    await loading;
    await harness.fixture.whenStable();

    expect(text(page.querySelector('h1'))).toBe('Books');
  });

  it('stands a grid of placeholders in for the works while they load', async () => {
    const navigating = harness.navigateByUrl('/library');
    const request = await worksRequest();
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;

    expect(page.querySelector('[aria-busy="true"]')).not.toBeNull();
    expect(page.querySelectorAll('[data-slot="skeleton"]').length).toBeGreaterThan(0);

    request.flush(pageOf([dune]));
    await shown(page);
    expect(page.querySelector('[aria-busy="true"]')).toBeNull();
  });

  it('Retry asks again for works that could not be loaded', async () => {
    const navigating = harness.navigateByUrl('/library?sort=added');
    (await worksRequest()).flush(null, { status: 500, statusText: 'Internal Server Error' });
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await shown(page);

    buttonLabelled(page, 'Retry').click();
    const request = await worksRequest();
    expect(request.request.params.get('Sort')).toBe('added');
    request.flush(pageOf([dune]));

    await vi.waitFor(() => expect(captions(page).map((caption) => caption[0])).toEqual(['Dune']));
    expect(page.querySelector('[role="alert"]')).toBeNull();
  });

  it('offers a search that found nothing a way back to the whole library', async () => {
    const { page } = await openList('/library?q=zzz', []);

    expect(linkLabelled(page, 'Clear search').getAttribute('href')).toBe('/library');
  });

  it('shows no table', async () => {
    const { page } = await openList('/library');

    expect(page.querySelector('table, [role="table"], [role="grid"]')).toBeNull();
  });

  it('shows a placeholder with the title for a work with no cover', async () => {
    const { page } = await openList('/library');

    const item = gridItems(page)[1];
    const cover = item.querySelector('app-work-cover')!;
    expect(cover.querySelector('img')).toBeNull();
    expect(text(cover)).toBe('Good Omens');
    expect(captions(page)[1]).toEqual(['Good Omens', 'Terry Pratchett, Neil Gaiman']);
  });

  it('links each work to its detail page', async () => {
    const { page } = await openList('/library');

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

  it('asks for every section when the URL names none', async () => {
    const { request } = await openList('/library');

    const params = request.request.params;
    expect(params.has('SectionId')).toBe(false);
    expect(params.has('NoSection')).toBe(false);
    expect(params.get('Sort')).toBe('title');
    expect(params.get('Offset')).toBe('0');
    expect(params.get('Limit')).toBe('50');
  });

  it("asks for one section's works when the URL names it", async () => {
    const { request } = await openList(`/library?section=${booksId}`, [dune, goodOmens]);

    expect(request.request.params.get('SectionId')).toBe(booksId);
    expect(request.request.params.has('NoSection')).toBe(false);
  });

  it("asks for the default area's works when the URL says section=default", async () => {
    const { request } = await openList('/library?section=default', [inDefault]);

    expect(request.request.params.get('NoSection')).toBe('true');
    expect(request.request.params.has('SectionId')).toBe(false);
  });

  it('sorting keeps the section in the URL', async () => {
    const { page } = await openList('/library?section=default&page=2', [inDefault], 120);

    buttonNamed(page, 'Recently added').click();
    const request = await answerNext(page, [inDefault], 120);

    expect(router.url).toBe('/library?section=default&sort=added');
    expect(request.request.params.get('NoSection')).toBe('true');
    expect(request.request.params.get('Sort')).toBe('added');
  });

  it('asks again when the URL switches section while the page is open', async () => {
    const { page } = await openList('/library');
    const listPage = harness.routeDebugElement?.componentInstance;

    const navigating = harness.navigateByUrl(`/library?section=${comicsId}`);
    const request = await worksRequest();
    expect(request.request.params.get('SectionId')).toBe(comicsId);
    request.flush(pageOf([theMartian]));
    await navigating;

    await vi.waitFor(() =>
      expect(captions(page).map((caption) => caption[0])).toEqual(['The Martian']),
    );
    expect(harness.routeDebugElement?.componentInstance).toBe(listPage);
  });

  it('asks for 50 works sorted by title when the URL has no parameters', async () => {
    const { page, request } = await openList('/library');

    const params = request.request.params;
    expect(params.get('Sort')).toBe('title');
    expect(params.get('Offset')).toBe('0');
    expect(params.get('Limit')).toBe('50');
    expect(chosenSorts(page)).toEqual(['Title']);
    expect(buttonNamed(page, 'Recently added')).toBeTruthy();
  });

  it('loads the section, sort and page the URL names', async () => {
    const { page, request } = await openList(
      `/library?section=${booksId}&sort=added&page=3`,
      [dune],
      101,
    );

    const params = request.request.params;
    expect(params.get('SectionId')).toBe(booksId);
    expect(params.get('Sort')).toBe('added');
    expect(params.get('Offset')).toBe('100');
    expect(params.get('Limit')).toBe('50');
    expect(chosenSorts(page)).toEqual(['Recently added']);
    expect(page.textContent).toContain('Page 3 of 3');
  });

  it('choosing Recently added puts sort=added in the URL and goes back to page 1', async () => {
    const { page } = await openList(`/library?section=${booksId}&page=2`, [dune], 120);

    buttonNamed(page, 'Recently added').click();
    const request = await answerNext(page, [goodOmens], 120);

    expect(router.url).toBe(`/library?section=${booksId}&sort=added`);
    const params = request.request.params;
    expect(params.get('SectionId')).toBe(booksId);
    expect(params.get('Sort')).toBe('added');
    expect(params.get('Offset')).toBe('0');
    expect(chosenSorts(page)).toEqual(['Recently added']);
    expect(page.textContent).toContain('Page 1 of 3');
  });

  it('choosing the sort that is already chosen keeps it chosen', async () => {
    const { page } = await openList('/library?sort=added');

    buttonNamed(page, 'Recently added').click();
    harness.detectChanges();

    expect(chosenSorts(page)).toEqual(['Recently added']);
    expect(router.url).toBe('/library?sort=added');
  });

  it('choosing Title leaves sort out of the URL', async () => {
    const { page } = await openList('/library?sort=added&page=2', [dune], 120);

    buttonNamed(page, 'Title').click();
    const request = await answerNext(page, [dune], 120);

    expect(router.url).toBe('/library');
    expect(request.request.params.get('Sort')).toBe('title');
    expect(chosenSorts(page)).toEqual(['Title']);
  });

  it('Next puts page=2 in the URL and asks for offset 50', async () => {
    const { page } = await openList('/library', [dune, goodOmens, theMartian], 120);

    buttonNamed(page, 'Next').click();
    const request = await answerNext(page, [theMartian], 120);

    expect(router.url).toBe('/library?page=2');
    expect(request.request.params.get('Sort')).toBe('title');
    expect(request.request.params.get('Offset')).toBe('50');
    expect(captions(page).map((caption) => caption[0])).toEqual(['The Martian']);
    expect(page.textContent).toContain('Page 2 of 3');
  });

  it('Previous from page 2 leaves page out of the URL and keeps the section and sort', async () => {
    const { page } = await openList(`/library?section=${booksId}&sort=added&page=2`, [dune], 120);

    buttonNamed(page, 'Previous').click();
    const request = await answerNext(page, [goodOmens], 120);

    expect(router.url).toBe(`/library?section=${booksId}&sort=added`);
    expect(request.request.params.get('Offset')).toBe('0');
  });

  it('Previous is disabled on the first page and Next on the last', async () => {
    const { page } = await openList('/library', [dune], 120);
    expect(buttonNamed(page, 'Previous').disabled).toBe(true);
    expect(buttonNamed(page, 'Next').disabled).toBe(false);

    await openList('/library?page=2', [dune], 120);
    expect(buttonNamed(page, 'Previous').disabled).toBe(false);
    expect(buttonNamed(page, 'Next').disabled).toBe(false);

    await openList('/library?page=3', [dune], 120);
    expect(buttonNamed(page, 'Previous').disabled).toBe(false);
    expect(buttonNamed(page, 'Next').disabled).toBe(true);
  });

  it('disables both Previous and Next when every work fits on one page', async () => {
    const { page } = await openList('/library', [dune, goodOmens], 2);

    expect(buttonNamed(page, 'Previous').disabled).toBe(true);
    expect(buttonNamed(page, 'Next').disabled).toBe(true);
  });

  it('shows the total', async () => {
    const { page } = await openList('/library', [dune], 120);
    expect(page.textContent).toContain('120 works');

    await openList(`/library?section=${booksId}`, [dune], 1);
    expect(page.textContent).toContain('1 work');
    expect(page.textContent).not.toContain('1 works');
  });

  it('has no page-size control', async () => {
    const { page } = await openList('/library', [dune], 120);

    expect(
      page.querySelectorAll(
        'input:not([type="search"]), select, [role="combobox"], [role="listbox"]',
      ),
    ).toHaveLength(0);
    expect(
      Array.from(page.querySelectorAll('button')).map((button) => button.textContent?.trim()),
    ).toEqual(['Title', 'Recently added', 'Previous', 'Next']);
    expect(page.textContent).not.toMatch(/per page|page size/i);
  });

  it.each([
    '/library?sort=newest&page=0',
    '/library?sort=TITLE&page=abc',
    '/library?sort=&page=-2',
    '/library?page=1.5',
  ])('treats an unknown sort or page as the default: %s', async (url) => {
    const { page, request } = await openList(url, [dune], 120);

    expect(request.request.params.get('Sort')).toBe('title');
    expect(request.request.params.get('Offset')).toBe('0');
    expect(chosenSorts(page)).toEqual(['Title']);
    expect(page.textContent).toContain('Page 1 of 3');
  });

  it('asks for the term the URL names, by relevance', async () => {
    const { page, request } = await openList('/library?q=hail', [inDefault]);

    expect(request.request.params.get('Search')).toBe('hail');
    expect(request.request.params.get('Sort')).toBe('relevance');
    expect(captions(page).map((caption) => caption[0])).toEqual(['Project Hail Mary']);
  });

  it('asks for the term trimmed', async () => {
    const { request } = await openList('/library?q=%20hail%20', [inDefault]);

    expect(request.request.params.get('Search')).toBe('hail');
  });

  it.each(['/library', '/library?q=', '/library?q=%20%20', '/library?sort=relevance'])(
    'sends no Search when the URL has no term or a blank one: %s',
    async (url) => {
      const { request } = await openList(url);

      expect(request.request.params.has('Search')).toBe(false);
      expect(request.request.params.get('Sort')).toBe('title');
    },
  );

  it('offers Relevance, selected, during a search and not without one', async () => {
    const { page } = await openList('/library?q=hail', [inDefault]);
    expect(
      Array.from(page.querySelectorAll('[role="group"] button')).map((button) =>
        button.textContent?.trim(),
      ),
    ).toEqual(['Relevance', 'Title', 'Recently added']);
    expect(chosenSorts(page)).toEqual(['Relevance']);

    await openList('/library');
    expect(
      Array.from(page.querySelectorAll('[role="group"] button')).map((button) =>
        button.textContent?.trim(),
      ),
    ).toEqual(['Title', 'Recently added']);
  });

  it('choosing Title during a search puts sort=title in the URL and keeps the term', async () => {
    const { page } = await openList('/library?q=hail&page=2', [inDefault], 120);

    buttonNamed(page, 'Title').click();
    const request = await answerNext(page, [inDefault], 120);

    expect(router.url).toBe('/library?q=hail&sort=title');
    expect(request.request.params.get('Search')).toBe('hail');
    expect(request.request.params.get('Sort')).toBe('title');
    expect(chosenSorts(page)).toEqual(['Title']);
  });

  it('choosing Relevance leaves sort out of the URL', async () => {
    const { page } = await openList('/library?q=hail&sort=title&page=2', [inDefault], 120);

    buttonNamed(page, 'Relevance').click();
    const request = await answerNext(page, [inDefault], 120);

    expect(router.url).toBe('/library?q=hail');
    expect(request.request.params.get('Sort')).toBe('relevance');
    expect(chosenSorts(page)).toEqual(['Relevance']);
  });

  it('Next keeps the term in the URL', async () => {
    const { page } = await openList('/library?q=hail', [inDefault], 120);

    buttonNamed(page, 'Next').click();
    const request = await answerNext(page, [inDefault], 120);

    expect(router.url).toBe('/library?q=hail&page=2');
    expect(request.request.params.get('Search')).toBe('hail');
    expect(request.request.params.get('Offset')).toBe('50');
  });

  it('asks again when the term in the URL changes', async () => {
    const { page } = await openList('/library?q=hail&sort=title&page=2', [inDefault], 120);
    const listPage = harness.routeDebugElement?.componentInstance;

    const navigating = harness.navigateByUrl('/library?q=weir');
    const request = await worksRequest();
    expect(request.request.params.get('Search')).toBe('weir');
    expect(request.request.params.get('Sort')).toBe('relevance');
    expect(request.request.params.get('Offset')).toBe('0');
    request.flush(pageOf([theMartian]));
    await navigating;

    await vi.waitFor(() =>
      expect(captions(page).map((caption) => caption[0])).toEqual(['The Martian']),
    );
    expect(chosenSorts(page)).toEqual(['Relevance']);
    expect(harness.routeDebugElement?.componentInstance).toBe(listPage);
  });

  it('says how many works match during a search', async () => {
    const { page } = await openList('/library?q=a', [dune, goodOmens, theMartian]);
    expect(page.textContent).toContain('3 works match');

    await openList('/library?q=dune', [dune]);
    expect(page.textContent).toContain('1 work matches');
  });

  it('says No works match with the term when a search finds none', async () => {
    const { page } = await openList('/library?q=%20zzz%20', []);

    expect(page.querySelector('ul')).toBeNull();
    expect(page.textContent).toContain('No works match "zzz"');
    expect(page.textContent).not.toContain('No works yet');
  });

  it('shows No works yet in place of the grid when there are none', async () => {
    const { page } = await openList('/library', []);

    expect(page.querySelector('ul')).toBeNull();
    expect(page.textContent).toContain('No works yet');
  });

  it('shows Add books to a contributor and not to a viewer', async () => {
    const { page } = await openList('/library', []);
    expect(buttonLabelled(page, 'Add books')).not.toBeNull();

    canUpload.set(false);
    harness.detectChanges();
    expect(page.textContent).toContain('No works yet');
    expect(page.textContent).not.toContain('Add books');
  });

  it('opens the Add books dialog from the list', async () => {
    const { page } = await openList('/library', []);
    buttonLabelled(page, 'Add books').click();
    expect(openAddBooks).toHaveBeenLastCalledWith([], null);

    const { page: inComics } = await openList(`/library?section=${comicsId}`, []);
    buttonLabelled(inComics, 'Add books').click();
    expect(openAddBooks).toHaveBeenLastCalledWith([], comicsId);

    const { page: inDefault } = await openList('/library?section=default', []);
    buttonLabelled(inDefault, 'Add books').click();
    expect(openAddBooks).toHaveBeenLastCalledWith([], 'default');
  });

  it('says something went wrong when the works cannot be loaded', async () => {
    const navigating = harness.navigateByUrl('/library');
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
