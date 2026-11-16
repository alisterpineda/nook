import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { components } from '../../api/schema';
import { text } from '../../testing/element-text';
import { PersonPage } from './person-page';

type ContributorRole = components['schemas']['ContributorRole'];
type PersonDetailResponse = components['schemas']['PersonDetailResponse'];
type WorkSummaryResponse = components['schemas']['WorkSummaryResponse'];

const herbertId = '0198a1b2-0000-7000-8000-000000000001';
const duneSeries = { id: '0198a1b2-0000-7000-8000-0000000000c1', name: 'Dune' };

function work(id: string, title: string, overrides: Partial<WorkSummaryResponse> = {}) {
  return {
    id,
    sectionId: null,
    title,
    authors: ['Frank Herbert'],
    series: null,
    coverHash: null,
    formats: ['epub'],
    addedAt: '2026-11-01T12:00:00Z',
    ...overrides,
  } satisfies WorkSummaryResponse;
}

describe('PersonPage', () => {
  const dune = work('0198a1b2-0000-7000-8000-0000000000a1', 'Dune', {
    series: { ...duneSeries, position: 1 },
    coverHash: 'abc123',
  });
  const messiah = work('0198a1b2-0000-7000-8000-0000000000a2', 'Dune Messiah', {
    series: { ...duneSeries, position: 2 },
  });
  const dragon = work('0198a1b2-0000-7000-8000-0000000000a3', 'The Dragon in the Sea');
  const sandworm = work('0198a1b2-0000-7000-8000-0000000000a4', 'Sandworm Songs', {
    authors: ['Ada Lovelace', 'Frank Herbert'],
  });

  const herbert: PersonDetailResponse = {
    id: herbertId,
    name: 'Frank Herbert',
    sortName: 'Herbert, Frank',
    roles: [{ role: 'author', works: [dune, messiah, dragon] }],
  };

  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'people/:id', component: PersonPage },
          { path: 'works/:id', children: [] },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => httpTesting.verify());

  function personRequest(id: string): Promise<TestRequest> {
    return vi.waitFor(() => httpTesting.expectOne(`/api/people/${id}`));
  }

  /** Waits for the page to show what the API answered: the person, "Person not found" or an error. */
  async function shown(page: HTMLElement): Promise<void> {
    await vi.waitFor(() => expect(page.querySelector('h1, [role="alert"]')).not.toBeNull());
  }

  /** Opens the page for `person` and answers its request with it. */
  async function openPerson(person: PersonDetailResponse): Promise<HTMLElement> {
    const navigating = harness.navigateByUrl(`/people/${person.id}`);
    (await personRequest(person.id)).flush(person);
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await shown(page);
    return page;
  }

  /** The titles of the works in `container`, in the order shown. */
  function titles(container: Element): string[] {
    return Array.from(container.querySelectorAll('app-work-card a > span:first-of-type')).map(text);
  }

  /** Each heading of `level` in `container` with the titles of the works it stands over. */
  function headed(container: Element, level: 'h2' | 'h3'): [string, string[]][] {
    return Array.from(container.querySelectorAll(level)).map((heading) => [
      text(heading),
      titles(level === 'h2' ? heading.parentElement! : heading.nextElementSibling!),
    ]);
  }

  it("shows the person's name as the heading", async () => {
    const page = await openPerson(herbert);

    expect(text(page.querySelector('h1'))).toBe('Frank Herbert');
  });

  it('shows each work as a card that links to the work', async () => {
    const page = await openPerson({
      ...herbert,
      roles: [{ role: 'author', works: [dune, sandworm] }],
    });

    const cards = Array.from(page.querySelectorAll('ul[aria-label="Works"] > li'));
    expect(cards.map((card) => card.querySelector('a')?.getAttribute('href'))).toEqual([
      `/works/${dune.id}`,
      `/works/${sandworm.id}`,
    ]);
    for (const card of cards) {
      expect(card.querySelector('app-work-cover')).not.toBeNull();
    }
    expect(cards.map((card) => Array.from(card.querySelectorAll('a > span')).map(text))).toEqual([
      ['Dune', 'Frank Herbert'],
      ['Sandworm Songs', 'Ada Lovelace, Frank Herbert'],
    ]);
  });

  it('shows no role heading for a person with one role', async () => {
    const page = await openPerson(herbert);

    expect(page.querySelector('h2')).toBeNull();
    expect(titles(page)).toEqual(['Dune', 'Dune Messiah', 'The Dragon in the Sea']);
  });

  it('shows a heading for each role in the order the API sent them', async () => {
    const page = await openPerson({
      ...herbert,
      roles: [
        { role: 'author', works: [dragon] },
        { role: 'translator', works: [sandworm] },
      ],
    });

    expect(headed(page, 'h2')).toEqual([
      ['Author', ['The Dragon in the Sea']],
      ['Translator', ['Sandworm Songs']],
    ]);
  });

  it('shows a role it has no label for as the API sent it', async () => {
    const page = await openPerson({
      ...herbert,
      roles: [
        { role: 'author', works: [dragon] },
        { role: 'illustrator' as ContributorRole, works: [sandworm] },
      ],
    });

    expect(Array.from(page.querySelectorAll('h2')).map(text)).toEqual(['Author', 'illustrator']);
  });

  it('puts each series under its name and the rest under Other works', async () => {
    const page = await openPerson(herbert);

    expect(headed(page, 'h3')).toEqual([
      ['Dune', ['Dune', 'Dune Messiah']],
      ['Other works', ['The Dragon in the Sea']],
    ]);
  });

  it('starts a new heading for each series even when two share a name', async () => {
    const otherDune = work('0198a1b2-0000-7000-8000-0000000000a5', 'Dune Encyclopedia', {
      series: { id: '0198a1b2-0000-7000-8000-0000000000c2', name: 'Dune', position: null },
    });
    const page = await openPerson({
      ...herbert,
      roles: [{ role: 'author', works: [dune, messiah, otherDune] }],
    });

    expect(headed(page, 'h3')).toEqual([
      ['Dune', ['Dune', 'Dune Messiah']],
      ['Dune', ['Dune Encyclopedia']],
    ]);
  });

  it('shows no series heading when no work is in a series', async () => {
    const page = await openPerson({
      ...herbert,
      roles: [{ role: 'author', works: [dragon, sandworm] }],
    });

    expect(page.querySelector('h3')).toBeNull();
    expect(titles(page)).toEqual(['The Dragon in the Sea', 'Sandworm Songs']);
  });

  it('shows the series name as plain text', async () => {
    const page = await openPerson(herbert);

    const heading = page.querySelector('h3')!;
    expect(text(heading)).toBe('Dune');
    expect(heading.querySelector('a')).toBeNull();
  });

  it('shows Person not found when the API answers 404', async () => {
    const navigating = harness.navigateByUrl('/people/missing');
    (await personRequest('missing')).flush(
      { title: 'Not Found', status: 404 },
      { status: 404, statusText: 'Not Found' },
    );
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await shown(page);

    expect(text(page.querySelector('h1'))).toBe('Person not found');
    expect(page.querySelector('ul')).toBeNull();
  });

  it('says something went wrong when the person cannot be loaded', async () => {
    const navigating = harness.navigateByUrl(`/people/${herbertId}`);
    (await personRequest(herbertId)).flush(null, {
      status: 500,
      statusText: 'Internal Server Error',
    });
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await shown(page);

    expect(page.querySelector('h1')).toBeNull();
    expect(text(page.querySelector('[role="alert"]'))).toBe('Something went wrong. Try again.');
  });

  it('shows the next person when the URL switches to them while the page is open', async () => {
    const page = await openPerson(herbert);
    const personPage = harness.routeDebugElement?.componentInstance;

    const otherId = '0198a1b2-0000-7000-8000-000000000002';
    const navigating = harness.navigateByUrl(`/people/${otherId}`);
    (await personRequest(otherId)).flush({
      id: otherId,
      name: 'Ada Lovelace',
      sortName: 'Lovelace, Ada',
      roles: [{ role: 'author', works: [sandworm] }],
    } satisfies PersonDetailResponse);
    await navigating;

    await vi.waitFor(() => expect(text(page.querySelector('h1'))).toBe('Ada Lovelace'));
    expect(titles(page)).toEqual(['Sandworm Songs']);
    expect(harness.routeDebugElement?.componentInstance).toBe(personPage);
  });
});
