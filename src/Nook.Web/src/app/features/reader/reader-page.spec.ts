import { Location } from '@angular/common';
import { provideLocationMocks } from '@angular/common/testing';
import { Component, Provider } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { buttonLabelled } from '../../testing/button-labelled';
import { text } from '../../testing/element-text';
import { READER_ENGINE, ReaderPlace } from './reader-engine';
import { ReaderPage } from './reader-page';
import { FakeReaderEngine, provideFakeReaderEngine } from './testing/fake-reader-engine';

const workId = '0198a1b2-0000-7000-8000-000000000001';
const itemId = '0198a1b2-0000-7000-8000-0000000000f1';
const readerUrl = `/works/${workId}/read/${itemId}`;

@Component({ template: `<h1>Dune</h1>` })
class WorkDetailStub {}

describe('ReaderPage', () => {
  let engine: FakeReaderEngine;
  let harness: RouterTestingHarness;

  /** Sets up the router with the reader and a stand-in work page, the engine loaded by `providers`. */
  async function setUp(providers: Provider[]): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'works/:id', component: WorkDetailStub },
          { path: 'works/:workId/read/:itemId', component: ReaderPage },
        ]),
        provideLocationMocks(),
        ...providers,
      ],
    });
    harness = await RouterTestingHarness.create();
  }

  beforeEach(() => {
    engine = new FakeReaderEngine();
    localStorage.clear();
  });

  afterEach(() => localStorage.clear());

  function savedPlace(): unknown {
    const value = localStorage.getItem(`nook.reader.position.${itemId}`);
    return value === null ? null : JSON.parse(value);
  }

  function page(): HTMLElement {
    harness.detectChanges();
    return harness.routeNativeElement as HTMLElement;
  }

  /** Opens the reader and waits until the engine has been asked to open the book. */
  async function openReader(): Promise<HTMLElement> {
    await harness.navigateByUrl(readerUrl);
    await vi.waitFor(() => {
      page();
      expect(engine.opened).not.toBeNull();
    });
    // Lets the page take in what `open` resolved with.
    await settle();
    return page();
  }

  async function settle(): Promise<void> {
    for (let turn = 0; turn < 5; turn++) {
      await Promise.resolve();
    }
    harness.detectChanges();
  }

  function backLink(element: HTMLElement): HTMLAnchorElement | null {
    return Array.from(element.querySelectorAll('a')).find(
      (link) => text(link) === 'Back to the work',
    ) as HTMLAnchorElement | null;
  }
  /** The page turns the engine received, in order. */
  function turns(): string[] {
    return engine.calls
      .map((call) => call.method)
      .filter((method) => ['next', 'previous', 'goLeft', 'goRight'].includes(method));
  }

  function place(fraction: number, chapterTitle: string | null): ReaderPlace {
    return { locator: { href: 'OEBPS/chapter-1.xhtml' }, fraction, chapterTitle };
  }

  describe('with an engine that opens', () => {
    beforeEach(() => setUp([provideFakeReaderEngine(engine)]));

    it("opens the item's publication manifest in the engine", async () => {
      const element = await openReader();

      expect(engine.opened?.manifestUrl).toBe(`/api/items/${itemId}/publication/manifest.json`);
      expect(element.contains(engine.opened!.host)).toBe(true);
      expect(engine.opened?.options.place).toBeUndefined();
      expect(engine.opened?.options.settings).toEqual({
        layout: 'paginated',
        fontSize: 100,
        theme: 'light',
      });
      expect(element.querySelector('[role="alert"]')).toBeNull();
    });

    it('links back to the work', async () => {
      const element = await openReader();

      const link = backLink(element);
      expect(link?.getAttribute('href')).toBe(`/works/${workId}`);

      link!.click();
      await vi.waitFor(() => expect(TestBed.inject(Location).path()).toBe(`/works/${workId}`));
    });

    it('shows an error when the engine fails while reading', async () => {
      await openReader();

      engine.emitError(new Error('The chapter could not be loaded'));
      const element = page();

      expect(text(element.querySelector('[role="alert"]'))).toBe('This book could not be shown.');
      expect(backLink(element)?.getAttribute('href')).toBe(`/works/${workId}`);
      expect(element.querySelectorAll('button')).toHaveLength(0);
      expect(engine.isClosed).toBe(true);
    });

    it('shows the other book when the address moves to another item', async () => {
      await openReader();
      const otherWorkId = '0198a1b2-0000-7000-8000-000000000002';
      const otherItemId = '0198a1b2-0000-7000-8000-0000000000f9';
      engine.emitPlace(place(0.5, 'Chapter 1'));

      await harness.navigateByUrl(`/works/${otherWorkId}/read/${otherItemId}`);
      await settle();
      const element = page();

      expect(engine.calls.map((call) => call.method)).toEqual(['open', 'close', 'open']);
      expect(engine.opened?.manifestUrl).toBe(
        `/api/items/${otherItemId}/publication/manifest.json`,
      );
      expect(engine.isClosed).toBe(false);
      expect(backLink(element)?.getAttribute('href')).toBe(`/works/${otherWorkId}`);
      expect(text(element.querySelector('[data-reader-progress]'))).toBe('');

      engine.emitPlace(place(0.2, 'Prologue'));
      expect(localStorage.getItem(`nook.reader.position.${otherItemId}`)).not.toBeNull();
    });

    it('closes the engine when leaving', async () => {
      await openReader();
      expect(engine.isClosed).toBe(false);

      await harness.navigateByUrl(`/works/${workId}`);

      expect(engine.isClosed).toBe(true);
    });

    it('turns the page from the previous and next buttons', async () => {
      const element = await openReader();

      buttonLabelled(element, 'Next page').click();
      buttonLabelled(element, 'Previous page').click();

      expect(turns()).toEqual(['next', 'previous']);
    });

    it('turns the page when the engine reports a left or right arrow key', async () => {
      await openReader();

      engine.emitKey('right');
      engine.emitKey('left');

      expect(turns()).toEqual(['goRight', 'goLeft']);
    });

    it('turns the page on a tap in the left or right third and not in the middle', async () => {
      await openReader();

      engine.emitTap('left');
      engine.emitTap('middle');
      engine.emitTap('right');

      expect(turns()).toEqual(['goLeft', 'goRight']);
    });

    it('shows the percentage and the chapter title as the place changes', async () => {
      const element = await openReader();
      const readout = () => [
        text(element.querySelector('[data-reader-progress]')),
        text(element.querySelector('[data-reader-chapter]')),
      ];

      engine.emitPlace(place(0.123, 'Prologue'));
      page();
      expect(readout()).toEqual(['12%', 'Prologue']);

      engine.emitPlace(place(0.5, 'Chapter 1: Arrakis'));
      page();
      expect(readout()).toEqual(['50%', 'Chapter 1: Arrakis']);

      engine.emitPlace(place(1, null));
      page();
      expect(readout()).toEqual(['100%', '']);
    });

    it('opens the book at the saved place', async () => {
      const saved = {
        href: 'OEBPS/chapter-2.xhtml',
        type: 'application/xhtml+xml',
        locations: { progression: 0.25, totalProgression: 0.6 },
      };
      localStorage.setItem(`nook.reader.position.${itemId}`, JSON.stringify(saved));

      await openReader();

      expect(engine.opened?.options.place).toEqual(saved);
    });

    it('saves the place as the reader moves', async () => {
      await openReader();
      const first = place(0.1, 'Prologue');

      engine.emitPlace(first);

      expect(savedPlace()).toEqual(first.locator);
    });

    it('saves the last place when leaving, even within a second of the one before', async () => {
      await openReader();
      const last: ReaderPlace = {
        locator: { href: 'OEBPS/chapter-3.xhtml', locations: { progression: 0.5 } },
        fraction: 0.7,
        chapterTitle: 'Chapter 3',
      };

      engine.emitPlace(place(0.1, 'Prologue'));
      engine.emitPlace(last);
      await harness.navigateByUrl(`/works/${workId}`);

      expect(savedPlace()).toEqual(last.locator);
    });
  });

  describe('with an engine that cannot open the book', () => {
    beforeEach(() => {
      engine.openError = new Error('The manifest request failed');
      return setUp([provideFakeReaderEngine(engine)]);
    });

    it('shows an error and the way back when the book cannot be opened', async () => {
      const element = await openReader();

      expect(text(element.querySelector('[role="alert"]'))).toBe('This book could not be shown.');
      expect(backLink(element)?.getAttribute('href')).toBe(`/works/${workId}`);
      expect(element.querySelectorAll('button')).toHaveLength(0);
    });
  });

  describe('with an engine that cannot be loaded', () => {
    beforeEach(() =>
      setUp([
        { provide: READER_ENGINE, useValue: () => Promise.reject(new Error('Chunk failed')) },
      ]),
    );

    it('shows an error and the way back when the engine cannot be loaded', async () => {
      await harness.navigateByUrl(readerUrl);

      await vi.waitFor(() =>
        expect(text(page().querySelector('[role="alert"]'))).toBe('This book could not be shown.'),
      );
      expect(backLink(page())?.getAttribute('href')).toBe(`/works/${workId}`);
    });
  });
});
