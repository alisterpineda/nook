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
import { AuthorsListPage } from './authors-list-page';

type PageOfPersonSummaryResponse = components['schemas']['PageOfPersonSummaryResponse'];
type PersonSummaryResponse = components['schemas']['PersonSummaryResponse'];

@Component({ template: '<h1>Person</h1>' })
class PersonStub {}

describe('AuthorsListPage', () => {
  const herbert: PersonSummaryResponse = {
    id: '0198a1b2-0000-7000-8000-000000000001',
    name: 'Frank Herbert',
    sortName: 'Herbert, Frank',
    workCount: 1,
  };
  const leGuin: PersonSummaryResponse = {
    id: '0198a1b2-0000-7000-8000-000000000002',
    name: 'Ursula K. Le Guin',
    sortName: 'Le Guin, Ursula K.',
    workCount: 3,
  };
  const weir: PersonSummaryResponse = {
    id: '0198a1b2-0000-7000-8000-000000000003',
    name: 'Andy Weir',
    sortName: 'Weir, Andy',
    workCount: 2,
  };

  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let router: Router;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'authors', component: AuthorsListPage },
          { path: 'people/:id', component: PersonStub },
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
    items: PersonSummaryResponse[],
    total = items.length,
    offset = 0,
  ): PageOfPersonSummaryResponse {
    return { items, total, offset, limit: 50 };
  }

  function peopleRequest(): Promise<TestRequest> {
    return vi.waitFor(() => httpTesting.expectOne((request) => request.url === '/api/people'));
  }

  /** Opens the list at `url` and answers its request for people with `people`, out of `total` in all. */
  async function openList(
    url: string,
    people: PersonSummaryResponse[] = [herbert, leGuin],
    total = people.length,
  ): Promise<{ page: HTMLElement; request: TestRequest }> {
    const navigating = harness.navigateByUrl(url);
    const request = await peopleRequest();
    request.flush(pageOf(people, total, Number(request.request.params.get('Offset'))));
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await shown(page);
    return { page, request };
  }

  /** Waits for the page to show what the API answered: the list, "No authors yet" or an error. */
  async function shown(page: HTMLElement): Promise<void> {
    await vi.waitFor(() => expect(page.querySelector('ul, p')).not.toBeNull());
  }

  /** Answers the request the page makes after a control changes the URL, and waits for the page to show it. */
  async function answerNext(
    page: HTMLElement,
    people: PersonSummaryResponse[],
    total: number,
  ): Promise<TestRequest> {
    const request = await peopleRequest();
    request.flush(pageOf(people, total, Number(request.request.params.get('Offset'))));
    await shown(page);
    return request;
  }

  function rows(page: HTMLElement): Element[] {
    return Array.from(page.querySelectorAll('ul[aria-label="Authors"] > li'));
  }

  /** What each row says: the name, then the work count. */
  function rowTexts(page: HTMLElement): string[][] {
    return rows(page).map((row) => Array.from(row.children).map(text));
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

  it('the heading reads Authors', async () => {
    const { page } = await openList('/authors');

    expect(text(page.querySelector('h1'))).toBe('Authors');
  });

  it('asks for 50 authors from the start when the URL has no page', async () => {
    const { request } = await openList('/authors');

    const params = request.request.params;
    expect(params.get('Role')).toBe('author');
    expect(params.get('Offset')).toBe('0');
    expect(params.get('Limit')).toBe('50');
  });

  it("shows each author's name and work count in the order the API sent them", async () => {
    const { page } = await openList('/authors', [herbert, leGuin]);

    expect(rowTexts(page)).toEqual([
      ['Frank Herbert', '1 work'],
      ['Ursula K. Le Guin', '3 works'],
    ]);
    expect(page.querySelector('img, app-work-cover')).toBeNull();
  });

  it('links each author to their page', async () => {
    const { page } = await openList('/authors', [herbert, leGuin]);

    const links = rows(page).map((row) => row.querySelector<HTMLAnchorElement>('a')!);
    expect(links.map((link) => link.getAttribute('href'))).toEqual([
      `/people/${herbert.id}`,
      `/people/${leGuin.id}`,
    ]);
    expect(links.map(text)).toEqual(['Frank Herbert', 'Ursula K. Le Guin']);

    links[1].click();
    await vi.waitFor(() => expect(router.url).toBe(`/people/${leGuin.id}`));
  });

  it('Next puts page=2 in the URL and asks for offset 50', async () => {
    const { page } = await openList('/authors', [herbert, leGuin], 120);

    buttonNamed(page, 'Next').click();
    const request = await answerNext(page, [weir], 120);

    expect(router.url).toBe('/authors?page=2');
    expect(request.request.params.get('Role')).toBe('author');
    expect(request.request.params.get('Offset')).toBe('50');
    expect(request.request.params.get('Limit')).toBe('50');
    expect(rowTexts(page).map((row) => row[0])).toEqual(['Andy Weir']);
    expect(page.textContent).toContain('Page 2 of 3');
  });

  it('Previous from page 2 leaves page out of the URL', async () => {
    const { page } = await openList('/authors?page=2', [weir], 120);

    buttonNamed(page, 'Previous').click();
    const request = await answerNext(page, [herbert], 120);

    expect(router.url).toBe('/authors');
    expect(request.request.params.get('Offset')).toBe('0');
  });

  it('Previous is disabled on the first page and Next on the last', async () => {
    const { page } = await openList('/authors', [herbert], 120);
    expect(buttonNamed(page, 'Previous').disabled).toBe(true);
    expect(buttonNamed(page, 'Next').disabled).toBe(false);

    await openList('/authors?page=2', [herbert], 120);
    expect(buttonNamed(page, 'Previous').disabled).toBe(false);
    expect(buttonNamed(page, 'Next').disabled).toBe(false);

    await openList('/authors?page=3', [herbert], 120);
    expect(buttonNamed(page, 'Previous').disabled).toBe(false);
    expect(buttonNamed(page, 'Next').disabled).toBe(true);
  });

  it.each(['/authors?page=0', '/authors?page=abc', '/authors?page=-2', '/authors?page=1.5'])(
    'treats a page that is not a whole number above 0 as the first: %s',
    async (url) => {
      const { page, request } = await openList(url, [herbert], 120);

      expect(request.request.params.get('Offset')).toBe('0');
      expect(page.textContent).toContain('Page 1 of 3');
    },
  );

  it('shows the total', async () => {
    const { page } = await openList('/authors', [herbert], 51);
    expect(page.textContent).toContain('51 authors');

    await openList('/authors?page=2', [leGuin], 1);
    expect(page.textContent).toContain('1 author');
    expect(page.textContent).not.toContain('1 authors');
  });

  it('shows No authors yet in place of the list when there are none', async () => {
    const { page } = await openList('/authors', []);

    expect(page.querySelector('ul')).toBeNull();
    expect(page.textContent).toContain('No authors yet');
  });

  it('says something went wrong when the authors cannot be loaded', async () => {
    const navigating = harness.navigateByUrl('/authors');
    (await peopleRequest()).flush(null, { status: 500, statusText: 'Internal Server Error' });
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await shown(page);

    expect(page.querySelector('ul')).toBeNull();
    expect(page.querySelector('[role="alert"]')?.textContent?.trim()).toBe(
      'Something went wrong. Try again.',
    );
  });
});
