import { Location } from '@angular/common';
import { provideLocationMocks } from '@angular/common/testing';
import { Component, Provider, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { buttonLabelled } from '../../testing/button-labelled';
import { text } from '../../testing/element-text';
import {
  READER_ENGINE,
  ReaderContentsEntry,
  ReaderPlace,
  ReaderSettings,
  readerThemeColours,
} from './reader-engine';
import { AppTheme } from '../../core/app-theme';
import { ReaderPage } from './reader-page';
import { KeptReaderSettings } from './reader-settings-store';
import { FakeReaderEngine, provideFakeReaderEngine } from './testing/fake-reader-engine';

const workId = '0198a1b2-0000-7000-8000-000000000001';
const itemId = '0198a1b2-0000-7000-8000-0000000000f1';
const readerUrl = `/works/${workId}/read/${itemId}`;
const settingsKey = 'nook.reader.settings';

/** How a book is shown when nothing is kept and the app is light, with `changed` in place of what it names. */
function settingsWith(changed: Partial<ReaderSettings> = {}): ReaderSettings {
  return {
    layout: 'paginated',
    fontSize: 100,
    theme: 'light',
    font: 'book',
    lineHeight: 'book',
    margins: 'normal',
    ...changed,
  };
}

@Component({ template: `<h1>Dune</h1>` })
class WorkDetailStub {}

describe('ReaderPage', () => {
  let engine: FakeReaderEngine;
  let harness: RouterTestingHarness;
  /** Whether the app is in its dark colours. */
  const appIsDark = signal(false);

  /** Sets up the router with the reader and a stand-in work page, the engine loaded by `providers`. */
  async function setUp(providers: Provider[]): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'works/:id', component: WorkDetailStub },
          { path: 'works/:workId/read/:itemId', component: ReaderPage },
        ]),
        provideLocationMocks(),
        { provide: AppTheme, useValue: { dark: appIsDark } },
        ...providers,
      ],
    });
    harness = await RouterTestingHarness.create();
  }

  // The settings' selects bring a chosen item into view, which the test DOM has no way to do.
  beforeAll(() => {
    Element.prototype.scrollIntoView ??= () => undefined;
  });

  beforeEach(() => {
    engine = new FakeReaderEngine();
    appIsDark.set(false);
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

  /** The settings the engine was asked to apply, in order. */
  function applied(): ReaderSettings[] {
    return engine.calls.flatMap((call) => (call.method === 'apply' ? [call.settings] : []));
  }

  function place(fraction: number, chapterTitle: string | null): ReaderPlace {
    return { locator: { href: 'OEBPS/chapter-1.xhtml' }, fraction, chapterTitle };
  }

  function buttons(element: HTMLElement): string[] {
    return Array.from(element.querySelectorAll('button')).map((button) => text(button));
  }

  /** The contents panel, which is shown over the page rather than inside it, or null when it is not open. */
  function contentsPanel(): HTMLElement | null {
    return (
      Array.from(document.querySelectorAll<HTMLElement>('[role="dialog"]')).find((dialog) =>
        dialog.querySelector('[data-testid="reader-toc"]'),
      ) ?? null
    );
  }

  /** The display settings panel, shown over the page like the contents, or null when it is not open. */
  function settingsPanel(): HTMLElement | null {
    return document.querySelector<HTMLElement>('[data-testid="reader-settings"]');
  }

  /** Presses the display settings button and gives the panel once it is open. */
  async function openedSettingsPanel(): Promise<HTMLElement> {
    buttonLabelled(page(), 'Display settings').click();
    await vi.waitFor(() => {
      page();
      expect(settingsPanel()).not.toBeNull();
    });
    return settingsPanel()!;
  }

  /** The control a setting of the settings panel is chosen with: a select's button, or a group of buttons. */
  function control(panel: HTMLElement, setting: string): HTMLElement {
    const label = Array.from(panel.querySelectorAll('label')).find((one) => text(one) === setting);
    const found = label
      ? panel.querySelector<HTMLElement>(`#${label.htmlFor}`)
      : panel.querySelector<HTMLElement>(`[aria-label="${setting}"]`);
    if (!found) {
      throw new Error(`No setting called ${setting}`);
    }
    return found;
  }

  /** The choices of an open select's list, which is shown over the page. */
  function listed(): HTMLElement[] {
    return Array.from(document.querySelectorAll<HTMLElement>('[role="option"]'));
  }

  /** Makes a choice for a setting of the settings panel: from its select's list, or by pressing its button. */
  async function choose(panel: HTMLElement, setting: string, label: string): Promise<void> {
    const chooser = control(panel, setting);
    if (chooser.getAttribute('role') !== 'combobox') {
      buttonLabelled(chooser, label).click();
      page();
      return;
    }
    chooser.click();
    await vi.waitFor(() => {
      page();
      expect(listed().map((option) => text(option))).toContain(label);
    });
    listed()
      .find((option) => text(option) === label)!
      .click();
    await vi.waitFor(() => {
      page();
      expect(listed()).toEqual([]);
    });
  }

  /** The label of the choice a setting of the settings panel shows as the one in use. */
  function chosen(panel: HTMLElement, setting: string): string {
    page();
    const chooser = control(panel, setting);
    return text(
      chooser.getAttribute('role') === 'combobox'
        ? chooser
        : chooser.querySelector('[aria-pressed="true"]'),
    );
  }

  async function openedContentsPanel(): Promise<HTMLElement> {
    await vi.waitFor(() => {
      page();
      expect(contentsPanel()).not.toBeNull();
    });
    return contentsPanel()!;
  }

  /** The entries of a contents list: each entry's label, or its label and the entries under it. */
  type Outline = (string | [string, Outline])[];

  function outline(list: Element): Outline {
    return Array.from(list.children).map((item) => {
      const label = text(item.querySelector(':scope > button'));
      const nested = item.querySelector('ul');
      return nested ? [label, outline(nested)] : label;
    });
  }

  const contents: ReaderContentsEntry[] = [
    { label: 'Prologue', href: 'OEBPS/prologue.xhtml', children: [] },
    {
      label: 'Part One: Dune',
      href: 'OEBPS/part-1.xhtml',
      children: [
        { label: 'Chapter 1: Arrakis', href: 'OEBPS/part-1.xhtml#chapter-1', children: [] },
        { label: 'Chapter 2: The Gom Jabbar', href: 'OEBPS/part-1.xhtml#chapter-2', children: [] },
      ],
    },
    { label: 'Appendix', href: 'OEBPS/appendix.xhtml', children: [] },
  ];

  describe('with an engine that opens', () => {
    beforeEach(() => setUp([provideFakeReaderEngine(engine)]));

    it("opens the item's publication manifest in the engine", async () => {
      const element = await openReader();

      expect(engine.opened?.manifestUrl).toBe(`/api/items/${itemId}/publication/manifest.json`);
      expect(element.contains(engine.opened!.host)).toBe(true);
      expect(engine.opened?.options.place).toBeUndefined();
      expect(engine.opened?.options.settings).toEqual(settingsWith());
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

    it('lists the contents with nesting when the contents button is pressed', async () => {
      engine.contents = contents;
      const element = await openReader();
      expect(contentsPanel()).toBeNull();

      buttonLabelled(element, 'Contents').click();
      const panel = await openedContentsPanel();

      expect(text(panel.querySelector('h2'))).toBe('Contents');
      expect(outline(panel.querySelector('ul')!)).toEqual([
        'Prologue',
        ['Part One: Dune', ['Chapter 1: Arrakis', 'Chapter 2: The Gom Jabbar']],
        'Appendix',
      ]);
    });

    it('goes to a chosen entry and closes the panel', async () => {
      engine.contents = contents;
      const element = await openReader();

      buttonLabelled(element, 'Contents').click();
      buttonLabelled(await openedContentsPanel(), 'Chapter 2: The Gom Jabbar').click();
      await vi.waitFor(() => {
        page();
        expect(contentsPanel()).toBeNull();
      });

      expect(engine.calls.filter((call) => call.method === 'goTo')).toEqual([
        { method: 'goTo', entry: contents[1].children[1] },
      ]);
    });

    it('shows no contents button for a book without contents', async () => {
      const element = await openReader();

      expect(buttons(element)).not.toContain('Contents');
      expect(buttons(element)).toContain('Next page');
    });

    it('opens the book with the saved settings', async () => {
      const saved: ReaderSettings = {
        layout: 'scrolled',
        fontSize: 150,
        theme: 'sepia',
        font: 'serif',
        lineHeight: 'loose',
        margins: 'wide',
      };
      localStorage.setItem(settingsKey, JSON.stringify(saved));

      const element = await openReader();

      expect(engine.opened?.options.settings).toEqual(saved);
      expect(element.getAttribute('data-reader-theme')).toBe('sepia');
      const panel = await openedSettingsPanel();
      expect(chosen(panel, 'Theme')).toBe('Sepia');
      expect(chosen(panel, 'Font')).toBe('Serif');
      expect(chosen(panel, 'Line height')).toBe('Loose');
      expect(chosen(panel, 'Margins')).toBe('Wide');
      expect(text(panel.querySelector('[data-testid="reader-font-size"]'))).toBe('150%');
      expect(chosen(panel, 'Layout')).toBe('Scrolled');
    });

    it('keeps the display settings in a panel opened from the toolbar', async () => {
      const element = await openReader();

      expect(buttons(element)).toEqual(['Display settings', 'Previous page', 'Next page']);
      expect(settingsPanel()).toBeNull();

      const panel = await openedSettingsPanel();
      expect(text(panel.querySelector('h2'))).toBe('Display');
      expect(buttons(panel)).toEqual([
        'Auto',
        'Book',
        'Smaller text',
        'Larger text',
        'Book',
        'Normal',
        'Paginated',
        'Scrolled',
        'Close',
      ]);

      control(panel, 'Line height').click();
      await vi.waitFor(() => {
        page();
        expect(listed().map((option) => text(option))).toEqual([
          'Book',
          'Tight',
          'Medium',
          'Loose',
        ]);
      });
      // The list is drawn outside the page, and takes the book's theme too.
      expect(listed()[0].closest('[data-reader-theme]')?.getAttribute('data-reader-theme')).toBe(
        'light',
      );
    });

    it('sets the font, the line height and the margins from their choices', async () => {
      await openReader();
      const panel = await openedSettingsPanel();
      expect(chosen(panel, 'Font')).toBe('Book');
      expect(chosen(panel, 'Line height')).toBe('Book');
      expect(chosen(panel, 'Margins')).toBe('Normal');

      await choose(panel, 'Font', 'Sans');
      await choose(panel, 'Line height', 'Tight');
      await choose(panel, 'Margins', 'Narrow');
      // A choice already in use stays as it is.
      await choose(panel, 'Margins', 'Narrow');
      await choose(panel, 'Line height', 'Book');

      expect(chosen(panel, 'Font')).toBe('Sans');
      expect(chosen(panel, 'Line height')).toBe('Book');
      expect(chosen(panel, 'Margins')).toBe('Narrow');
      expect(applied()).toEqual([
        settingsWith({ font: 'sans' }),
        settingsWith({ font: 'sans', lineHeight: 'tight' }),
        settingsWith({ font: 'sans', lineHeight: 'tight', margins: 'narrow' }),
        settingsWith({ font: 'sans', margins: 'narrow' }),
      ]);
      expect(JSON.parse(localStorage.getItem(settingsKey)!)).toEqual({
        ...settingsWith({ font: 'sans', margins: 'narrow' }),
        theme: 'auto',
      });
    });

    it('switches between paginated and scrolled', async () => {
      await openReader();
      const panel = await openedSettingsPanel();
      expect(chosen(panel, 'Layout')).toBe('Paginated');

      await choose(panel, 'Layout', 'Scrolled');
      expect(chosen(panel, 'Layout')).toBe('Scrolled');
      // The layout already chosen stays chosen.
      await choose(panel, 'Layout', 'Scrolled');
      expect(chosen(panel, 'Layout')).toBe('Scrolled');
      await choose(panel, 'Layout', 'Paginated');
      expect(chosen(panel, 'Layout')).toBe('Paginated');

      expect(applied()).toEqual([
        settingsWith({ layout: 'scrolled' }),
        settingsWith({ layout: 'paginated' }),
      ]);
    });

    it('changes font size by ten percent and stops at 70 and 250', async () => {
      localStorage.setItem(
        settingsKey,
        JSON.stringify({ layout: 'paginated', fontSize: 80, theme: 'light' }),
      );
      await openReader();
      const panel = await openedSettingsPanel();
      const smaller = buttonLabelled(panel, 'Smaller text');
      const larger = buttonLabelled(panel, 'Larger text');
      const size = () => text(panel.querySelector('[data-testid="reader-font-size"]'));
      expect(size()).toBe('80%');

      larger.click();
      smaller.click();
      smaller.click();
      page();
      expect(size()).toBe('70%');
      expect(smaller.disabled).toBe(true);
      expect(larger.disabled).toBe(false);
      smaller.click();

      expect(applied().map((settings) => settings.fontSize)).toEqual([90, 80, 70]);
    });

    it('stops at 250 percent', async () => {
      localStorage.setItem(
        settingsKey,
        JSON.stringify({ layout: 'paginated', fontSize: 240, theme: 'light' }),
      );
      await openReader();
      const larger = buttonLabelled(await openedSettingsPanel(), 'Larger text');

      larger.click();
      page();
      expect(larger.disabled).toBe(true);
      larger.click();

      expect(applied().map((settings) => settings.fontSize)).toEqual([250]);
    });

    it('shows the book in the chosen theme and recolours the reader', async () => {
      engine.contents = contents;
      const element = await openReader();
      expect(element.getAttribute('data-reader-theme')).toBe('light');
      const settings = await openedSettingsPanel();
      expect(chosen(settings, 'Theme')).toBe('Auto');

      const seen: string[] = [];
      for (const label of ['Dark', 'Sepia', 'Light']) {
        await choose(settings, 'Theme', label);
        expect(chosen(settings, 'Theme')).toBe(label);
        seen.push(element.getAttribute('data-reader-theme')!);
        // The panel is drawn outside the page, and takes the theme too.
        expect(settings.getAttribute('data-reader-theme')).toBe(seen.at(-1));
      }

      expect(seen).toEqual(['dark', 'sepia', 'light']);
      // The reader around the book is drawn in the colours the book is.
      expect(element.style.getPropertyValue('--background')).toBe(
        readerThemeColours.light.background,
      );
      expect(element.style.getPropertyValue('--foreground')).toBe(readerThemeColours.light.text);
      expect(applied().map((settings) => settings.theme)).toEqual(['dark', 'sepia', 'light']);

      await choose(settings, 'Theme', 'Dark');
      buttonLabelled(settings, 'Close').click();
      buttonLabelled(element, 'Contents').click();
      const panel = await openedContentsPanel();
      expect(panel.querySelector('hlm-sheet-content')?.getAttribute('data-reader-theme')).toBe(
        'dark',
      );
    });

    it("follows the app's light or dark while the theme is on auto", async () => {
      appIsDark.set(true);
      const element = await openReader();
      expect(engine.opened?.options.settings.theme).toBe('dark');
      expect(element.getAttribute('data-reader-theme')).toBe('dark');

      appIsDark.set(false);
      page();
      expect(element.getAttribute('data-reader-theme')).toBe('light');
      expect(element.style.getPropertyValue('--background')).toBe(
        readerThemeColours.light.background,
      );
      expect(applied()).toEqual([settingsWith({ theme: 'light' })]);

      const panel = await openedSettingsPanel();
      await choose(panel, 'Theme', 'Sepia');
      appIsDark.set(true);
      page();
      expect(element.getAttribute('data-reader-theme')).toBe('sepia');
      expect(applied().map((settings) => settings.theme)).toEqual(['light', 'sepia']);

      await choose(panel, 'Theme', 'Auto');
      page();
      expect(element.getAttribute('data-reader-theme')).toBe('dark');
      expect(applied().map((settings) => settings.theme)).toEqual(['light', 'sepia', 'dark']);
      const kept = JSON.parse(localStorage.getItem(settingsKey)!) as KeptReaderSettings;
      expect(kept.theme).toBe('auto');
    });

    it('saves a changed setting', async () => {
      await openReader();
      const panel = await openedSettingsPanel();

      await choose(panel, 'Layout', 'Scrolled');
      buttonLabelled(panel, 'Larger text').click();
      await choose(panel, 'Theme', 'Dark');

      expect(JSON.parse(localStorage.getItem(settingsKey)!)).toEqual(
        settingsWith({ layout: 'scrolled', fontSize: 110, theme: 'dark' }),
      );
    });

    it('applies a setting changed while the book was opening once it has opened', async () => {
      let finishOpening!: () => void;
      const opening = new Promise<void>((resolve) => (finishOpening = resolve));
      const open = engine.open.bind(engine);
      engine.open = async (...args) => {
        const opened = await open(...args);
        await opening;
        return opened;
      };
      await harness.navigateByUrl(readerUrl);
      await vi.waitFor(() => {
        page();
        expect(engine.opened).not.toBeNull();
      });

      await choose(await openedSettingsPanel(), 'Theme', 'Dark');
      expect(applied()).toEqual([]);
      finishOpening();
      await settle();

      expect(engine.opened?.options.settings.theme).toBe('light');
      expect(applied()).toEqual([settingsWith({ theme: 'dark' })]);
    });

    it("exposes the reader's state for browser checks in dev mode", async () => {
      engine.contents = contents;
      engine.details = {
        cfi: 'OEBPS/chapter-1.xhtml#0.25000',
        sectionIndex: 2,
        sections: 9,
        positions: 120,
        fontMechanism: 'body-zoom',
      };
      await openReader();
      expect(window.__nookReader?.ready).toBe(false);

      engine.emitPlace(place(0.25, 'Chapter 1: Arrakis'));

      expect(window.__nookReader).toMatchObject({
        ready: true,
        cfi: 'OEBPS/chapter-1.xhtml#0.25000',
        locator: { href: 'OEBPS/chapter-1.xhtml' },
        fraction: 0.25,
        flow: 'paginated',
        sectionIndex: 2,
        sections: 9,
        positions: 120,
        tocLabels: [
          'Prologue',
          'Part One: Dune',
          'Chapter 1: Arrakis',
          'Chapter 2: The Gom Jabbar',
          'Appendix',
        ],
        fontSize: 100,
        fontMechanism: 'body-zoom',
        theme: 'light',
        relocateCount: 1,
        errors: [],
      });

      buttonLabelled(await openedSettingsPanel(), 'Larger text').click();
      expect(window.__nookReader?.fontSize).toBe(110);

      await window.__nookReader?.goToFraction(0.9);
      expect(engine.calls).toContainEqual({ method: 'goToFraction', fraction: 0.9 });

      engine.emitError(new Error('The chapter could not be loaded'));
      expect(window.__nookReader?.errors).toEqual(['Error: The chapter could not be loaded']);

      await harness.navigateByUrl(`/works/${workId}`);
      expect(window.__nookReader).toBeUndefined();
    });

    it('lets go of a move that fails after the reader was left', async () => {
      await openReader();
      engine.emitPlace(place(0.25, 'Chapter 1: Arrakis'));
      let failMove: (error: unknown) => void = () => undefined;
      engine.settling = new Promise<void>((_, reject) => (failMove = reject));
      const jump = window.__nookReader?.goToFraction(0.9);

      engine.settling = null;
      await harness.navigateByUrl(`/works/${workId}`);
      failMove(new Error('The navigator was taken down'));
      await jump;

      expect(window.__nookReader).toBeUndefined();
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
