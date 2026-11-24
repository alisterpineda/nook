import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { SearchResponse } from '../api/search-api';
import { typeInto } from '../testing/type-into';
import { CommandPalette } from './command-palette';

@Component({ imports: [CommandPalette], template: '<app-command-palette />' })
class Host {}

@Component({ template: '' })
class Stub {}

describe('CommandPalette', () => {
  let fixture: ComponentFixture<Host>;
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    // jsdom has no scrollIntoView, which the command calls on the row it activates.
    Element.prototype.scrollIntoView ??= () => undefined;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'library', component: Stub },
          { path: 'works/:id', component: Stub },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Host);
    await fixture.whenStable();

    document.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true }),
    );
    await vi.waitFor(() => expect(input()).not.toBeNull());
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
  });

  afterEach(() => {
    vi.useRealTimers();
    httpTesting.verify();
  });

  const dialog = (): HTMLElement | null => document.querySelector('[role="dialog"]');
  const input = (): HTMLInputElement | null =>
    document.querySelector<HTMLInputElement>('[role="dialog"] input');
  const text = (): string => dialog()?.textContent ?? '';

  async function type(value: string): Promise<void> {
    typeInto(input()!, value);
    await wait(0);
  }

  async function wait(ms: number): Promise<void> {
    await vi.advanceTimersByTimeAsync(ms);
    await fixture.whenStable();
  }

  const searches = () => httpTesting.match((request) => request.url === '/api/search');

  const noGroups: SearchResponse = { groups: [] };

  it('asks for the trimmed term once the typing has paused', async () => {
    await type(' hail ');

    await wait(299);
    httpTesting.expectNone((request) => request.url === '/api/search');
    await wait(1);

    const request = httpTesting.expectOne((req) => req.url === '/api/search');
    expect(request.request.params.get('Q')).toBe('hail');
    expect(request.request.params.get('Limit')).toBe('6');
    request.flush(noGroups);
  });

  it('asks once for a term typed in several keystrokes', async () => {
    await type('ha');
    await wait(200);
    await type('hail');
    await wait(300);

    const requests = searches();
    expect(requests).toHaveLength(1);
    expect(requests[0].request.params.get('Q')).toBe('hail');
    requests[0].flush(noGroups);
  });

  it.each(['a', ' a '])('sends no request and keeps the hint for %j', async (value) => {
    await type(value);
    await wait(300);

    httpTesting.expectNone((request) => request.url === '/api/search');
    expect(text()).toContain('Type to search the library');
  });

  it('shows Searching… from the keystroke until the answer', async () => {
    await type('hail');
    expect(text()).toContain('Searching…');

    await wait(300);
    expect(text()).toContain('Searching…');
    expect(text()).not.toContain('No results found.');

    httpTesting.expectOne((req) => req.url === '/api/search').flush(noGroups);
    await wait(0);
    expect(text()).not.toContain('Searching…');
  });

  it('says something went wrong when the request fails', async () => {
    await type('hail');
    await wait(300);
    httpTesting
      .expectOne((req) => req.url === '/api/search')
      .flush('boom', { status: 500, statusText: 'Server Error' });
    await wait(0);
    expect(text()).toContain('Something went wrong. Try again.');

    await type('hailm');
    expect(text()).not.toContain('Something went wrong. Try again.');
    await wait(300);
    httpTesting.expectOne((req) => req.url === '/api/search').flush(noGroups);
  });

  it('says no results were found for an answer with no groups', async () => {
    await type('hail');
    await wait(300);
    httpTesting.expectOne((req) => req.url === '/api/search').flush(noGroups);
    await wait(0);

    expect(text()).toContain('No results found.');
  });

  it('never shows the answer for a term the user has left', async () => {
    await type('du');
    await wait(300);
    const first = httpTesting.expectOne((req) => req.url === '/api/search');
    await type('dune');
    await wait(300);
    const second = httpTesting.expectOne((req) => req.url === '/api/search');

    first.flush({
      groups: [
        {
          source: 'works',
          total: 1,
          results: [
            { id: '0198a1b2-0000-7000-8000-000000000001', title: 'Duplicate', subtitle: null },
          ],
        },
      ],
    } satisfies SearchResponse);
    await wait(0);
    expect(text()).not.toContain('Duplicate');
    expect(text()).toContain('Searching…');

    second.flush(noGroups);
    await wait(0);
    expect(text()).toContain('No results found.');
  });

  describe('results', () => {
    const omens = '0198a1b2-0000-7000-8000-000000000001';
    const dune = '0198a1b2-0000-7000-8000-000000000002';

    async function answer(term: string, total = 2): Promise<void> {
      await type(term);
      await wait(300);
      httpTesting
        .expectOne((req) => req.url === '/api/search')
        .flush({
          groups: [
            {
              source: 'works',
              total,
              results: [
                { id: omens, title: 'Good Omens', subtitle: 'Terry Pratchett, Neil Gaiman' },
                { id: dune, title: 'Dune', subtitle: null },
              ],
            },
          ],
        } satisfies SearchResponse);
      await wait(0);
    }

    const rows = (): HTMLElement[] =>
      Array.from(document.querySelectorAll<HTMLElement>('[role="dialog"] button[role="option"]'));

    function press(key: string, keyCode: number): void {
      input()!.dispatchEvent(new KeyboardEvent('keydown', { key, keyCode, bubbles: true }));
    }

    it('lists each work under Works with its title and authors', async () => {
      await answer('prat');

      const label = document.querySelector('[data-slot="command-group-label"]');
      expect(label?.textContent?.trim()).toBe('Works');
      const [first, second] = rows();
      expect(rows()).toHaveLength(2);
      expect(first.textContent).toContain('Good Omens');
      expect(first.textContent).toContain('Terry Pratchett, Neil Gaiman');
      expect(second.textContent?.trim()).toBe('Dune');
      expect(first.hasAttribute('data-hidden')).toBe(false);
      expect(second.hasAttribute('data-hidden')).toBe(false);
    });

    it('the first result is active when the results arrive', async () => {
      await answer('prat');

      const [first, second] = rows();
      expect(first.getAttribute('aria-selected')).toBe('true');
      expect(second.getAttribute('aria-selected')).not.toBe('true');
    });

    it('Enter opens the first result and closes the palette', async () => {
      await answer('prat');

      press('Enter', 13);
      await wait(0);

      await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe(`/works/${omens}`));
      await vi.waitFor(() => expect(dialog()).toBeNull());
    });

    it('Arrow down then Enter opens the second result', async () => {
      await answer('prat');

      press('ArrowDown', 40);
      press('Enter', 13);
      await wait(0);

      await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe(`/works/${dune}`));
    });

    it('clicking a result opens it and closes the palette', async () => {
      await answer('prat');

      rows()[1].click();
      await wait(0);

      await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe(`/works/${dune}`));
      await vi.waitFor(() => expect(dialog()).toBeNull());
    });

    it('a group with more matches than shown ends with a row to all of them', async () => {
      await answer(' hail mary ', 9);

      const last = rows().at(-1)!;
      expect(last.textContent?.trim()).toBe('View all (9) in Works');

      last.click();
      await wait(0);

      await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/library?q=hail%20mary'));
      await vi.waitFor(() => expect(dialog()).toBeNull());
    });

    it('a group shown in full has no row to all of them', async () => {
      await answer('hail mary', 2);

      expect(rows()).toHaveLength(2);
      expect(rows().some((row) => row.textContent?.trim().startsWith('View all'))).toBe(false);
    });
  });
});
