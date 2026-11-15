// The only module that imports Readium. It is loaded on demand by `READER_ENGINE`, so Readium stays out of the initial
// bundle, and it names just what it uses: importing the whole package pulls in parts the reader has no use for
// (read-aloud, audio, comics), which doubles what is loaded.
import {
  EpubNavigator,
  EpubPreferences,
  type EpubNavigatorConfiguration,
  type EpubNavigatorListeners,
  type IEpubPreferences,
  type KeyboardPeripheralEventData,
} from '@readium/navigator';
import {
  HttpFetcher,
  Locator,
  Manifest,
  Publication,
  type FetchImplementation,
  type Link,
  type TimelineItem,
} from '@readium/shared';
import {
  readerThemeColours,
  type ReaderContentsEntry,
  type ReaderEngine,
  type ReaderLocator,
  type ReaderOpenOptions,
  type ReaderOpened,
  type ReaderPlace,
  type ReaderSettings,
  type ReaderTapZone,
} from './reader-engine';

type FrameClickEvent = Parameters<EpubNavigatorListeners['tap']>[0];

const noop = (): void => undefined;

// The navigator listens for these keys on the page and inside every chapter frame, and reports both through its
// `peripheral` listener under these names.
const keyLeft = 'nook_left';
const keyRight = 'nook_right';
const arrowLeftKeyCode = 37;
const arrowRightKeyCode = 39;
// The arrow keys still turn the page while a toolbar button has focus; only text-entry controls keep them.
const textEntry = ['input', 'textarea', 'select', '[contenteditable]'];

// The kinds of link out of a book that a tap follows.
const linksOut = ['http:', 'https:', 'mailto:', 'tel:'];

// In scrolled layout a step forward or back scrolls this much of a screen, so a line or two stays in view.
const scrollStepOfScreen = 0.9;
// How close to a chapter's end or start, in pixels, counts as there.
const scrollEndSlack = 2;

/** What is kept while a book is open. */
interface OpenBook {
  readonly navigator: EpubNavigator;
  readonly publication: Publication;
  readonly positions: Locator[];
}

/**
 * The reader engine built on Readium's navigator. The book is fetched as the Readium publication the API serves: the
 * manifest from the URL given to `open`, and everything else from under the manifest's own `self` link, which is an
 * absolute URL carrying the token that lets the book's frames load files without the sign-in cookie.
 */
export class ReadiumEngine implements ReaderEngine {
  private book: OpenBook | null = null;
  private options: ReaderOpenOptions | null = null;
  private container: HTMLElement | null = null;
  private host: HTMLElement | null = null;
  private opened = false;
  private closed = false;
  /** A failure seen while the book was still opening; `open` rejects with it. */
  private failure: unknown = undefined;
  private chapterTitle: string | null = null;
  private locator: Locator | null = null;
  /** The settings the navigator was last given. */
  private settings: ReaderSettings | null = null;
  /** The settings changes in progress, run one after another so each starts from the place the last one restored. */
  private applying: Promise<void> = Promise.resolve();

  // The navigator catches most of its own failures and only logs them. What escapes it lands on the window: the
  // handlers it registers inside a chapter frame are functions from this window, so their exceptions report here.
  private readonly onWindowError = (event: ErrorEvent): void =>
    this.fail(event.error ?? event.message);
  private readonly onRejection = (event: PromiseRejectionEvent): void => this.fail(event.reason);

  async open(
    host: HTMLElement,
    manifestUrl: string,
    options: ReaderOpenOptions,
  ): Promise<ReaderOpened> {
    this.options = options;
    this.closed = false;
    this.opened = false;
    this.failure = undefined;
    this.locator = null;
    this.chapterTitle = null;
    this.settings = options.settings;
    this.applying = Promise.resolve();
    const view = host.ownerDocument.defaultView;
    if (!view) {
      throw new Error('The reader has no window to show the book in');
    }

    // The manifest, the positions list and each chapter are fetched through this; the files a chapter refers to
    // (stylesheets, images, fonts) are loaded by the browser from under the chapter's base URL instead.
    const fetchFile: FetchImplementation = async (input, init) => {
      const response = await view.fetch(input, { ...init, credentials: 'same-origin' });
      if (!response.ok) {
        this.fail(new Error(`${urlOf(input)} answered ${response.status}`));
      }
      return response;
    };

    const fetchedFrom = new URL(manifestUrl, host.ownerDocument.baseURI).href;
    const response = await view.fetch(fetchedFrom, { credentials: 'same-origin' });
    if (!response.ok) {
      throw new Error(`${fetchedFrom} answered ${response.status}`);
    }
    const json: unknown = await response.json();
    this.ensureOpen();

    // Files are only served under the self link, so it, not the URL fetched, is where the publication lives.
    const base = absoluteSelfLink(json);
    if (!base) {
      throw new Error(`${fetchedFrom} has no absolute self link`);
    }
    const manifest = Manifest.deserialize(json);
    if (!manifest) {
      throw new Error(`${fetchedFrom} is not a Readium Web Publication Manifest`);
    }
    // The publication's base URL, which every chapter frame's base and policy are written from, comes from this.
    if (manifest.links?.findWithRel('self')?.href !== base) {
      manifest.setSelfLink(base);
    }
    const publication = new Publication({ manifest, fetcher: new HttpFetcher(fetchFile, base) });

    // Found by its media type in the manifest's links.
    const positions = await publication.positionsFromManifest();
    this.ensureOpen();
    if (positions.length === 0) {
      throw new Error(`${fetchedFrom} links no positions list, or an empty one`);
    }

    const container = host.ownerDocument.createElement('div');
    // The navigator sets the container's width to the line length and positions its frames inside it.
    Object.assign(container.style, {
      contain: 'content',
      position: 'relative',
      width: '100%',
      height: '100%',
      margin: '0 auto',
    });
    host.append(container);
    host.addEventListener('click', this.onMarginClick);
    view.addEventListener('error', this.onWindowError);
    view.addEventListener('unhandledrejection', this.onRejection);
    this.container = container;
    this.host = host;

    const navigator = new EpubNavigator(
      container,
      publication,
      this.listeners(),
      positions,
      startingPlace(publication, options.place),
      { ...configuration, preferences: preferencesFor(options.settings) },
    );
    this.book = { navigator, publication, positions };
    // Resolves once the first chapter is shown at the starting place.
    await navigator.load();
    this.ensureOpen();
    if (this.failure !== undefined) {
      throw this.failure;
    }
    this.opened = true;
    return { contents: contentsOf(publication.toc?.items ?? []) };
  }

  next(): Promise<void> {
    return this.afterSettings(() =>
      this.step(1, (navigator, done) => navigator.goForward(false, done)),
    );
  }

  previous(): Promise<void> {
    return this.afterSettings(() =>
      this.step(-1, (navigator, done) => navigator.goBackward(false, done)),
    );
  }

  // In scrolled layout the book runs top to bottom, so left is back and right is forward.
  goLeft(): Promise<void> {
    return this.afterSettings(() =>
      this.step(-1, (navigator, done) => navigator.goLeft(false, done)),
    );
  }

  goRight(): Promise<void> {
    return this.afterSettings(() =>
      this.step(1, (navigator, done) => navigator.goRight(false, done)),
    );
  }

  goTo(entry: ReaderContentsEntry): Promise<void> {
    const book = this.book;
    if (!book) {
      return Promise.resolve();
    }
    // The navigator looks for the place inside the file only in `locations.fragments`: a fragment left on the href is
    // dropped, and the chapter opens at its start.
    const hash = entry.href.indexOf('#');
    const href = hash < 0 ? entry.href : entry.href.slice(0, hash);
    const fragment = hash < 0 ? '' : entry.href.slice(hash + 1);
    const link = book.publication.readingOrder.findWithHref(href);
    const locator = link
      ? Locator.deserialize({
          href,
          type: link.type ?? 'application/xhtml+xml',
          locations: fragment ? { fragments: [fragment] } : {},
        })
      : undefined;
    if (!locator) {
      return Promise.reject(new Error(`Contents entry ${entry.href} is not in the reading order`));
    }
    return this.afterSettings(() =>
      this.move((navigator, done) => navigator.go(locator, false, done)),
    );
  }

  goToFraction(fraction: number): Promise<void> {
    const book = this.book;
    if (!book) {
      return Promise.resolve();
    }
    const count = book.positions.length;
    let nearest = book.positions[0];
    let distance = Infinity;
    book.positions.forEach((position, index) => {
      const at = position.locations?.totalProgression ?? index / count;
      if (Math.abs(at - fraction) < distance) {
        distance = Math.abs(at - fraction);
        nearest = position;
      }
    });
    return this.afterSettings(() =>
      this.move((navigator, done) => navigator.go(nearest, false, done)),
    );
  }

  /**
   * Changes are run one after another: a second font-size change made before the first is done starts from the place
   * the first went back to, not from where the text drifted to.
   */
  apply(settings: ReaderSettings): Promise<void> {
    const change = this.applying.then(() => this.applyNow(settings));
    this.applying = change.catch(noop);
    return change;
  }

  private async applyNow(settings: ReaderSettings): Promise<void> {
    const navigator = this.book?.navigator;
    const before = this.settings;
    if (!navigator || this.closed || !before) {
      return;
    }
    const layoutChanged = settings.layout !== before.layout;
    const fontSizeChanged = settings.fontSize !== before.fontSize;
    const preferences: IEpubPreferences = {
      ...(layoutChanged ? layoutPreferences(settings) : {}),
      ...(fontSizeChanged ? fontSizePreferences(settings) : {}),
      ...(settings.theme !== before.theme ? themePreferences(settings) : {}),
    };
    if (Object.keys(preferences).length === 0) {
      return;
    }
    const place = navigator.currentLocator;
    await navigator.submitPreferences(new EpubPreferences(preferences));
    this.settings = settings;
    // The navigator does not keep the place across a font-size change: left alone, it lands about a third of a
    // chapter back. Going back to the place held before also makes sure a position is reported after a layout change.
    if ((layoutChanged || fontSizeChanged) && !this.closed) {
      await this.move((navigator, done) => navigator.go(place, false, done));
    }
  }

  async close(): Promise<void> {
    this.closed = true;
    const book = this.book;
    const host = this.host;
    const container = this.container;
    const view = host?.ownerDocument.defaultView;
    this.book = null;
    this.host = null;
    this.container = null;
    host?.removeEventListener('click', this.onMarginClick);
    view?.removeEventListener('error', this.onWindowError);
    view?.removeEventListener('unhandledrejection', this.onRejection);
    try {
      await book?.navigator.destroy();
    } finally {
      container?.remove();
    }
  }

  private listeners(): EpubNavigatorListeners {
    return {
      frameLoaded: noop,
      positionChanged: (locator) => this.moved(locator),
      viewportChanged: noop,
      timelineItemChanged: (item) => this.chapterChanged(item),
      tap: (event) => this.tappedInFrame(event),
      click: (event) => this.tappedInFrame(event),
      zoom: noop,
      miscPointer: noop,
      scroll: noop,
      customEvent: noop,
      handleLocator: (locator) => this.leaveBook(locator),
      textSelected: noop,
      contentProtection: noop,
      contextMenu: noop,
      peripheral: (data) => this.keyPressed(data),
    };
  }

  /**
   * A tap on a link the navigator does not follow itself, because it leads out of the book. A web, mail or phone link
   * opens in a new tab, so the book stays where it is; a link to anything else is left alone. Returning true tells the
   * navigator the link is dealt with.
   */
  private leaveBook(locator: Locator): boolean {
    const view = this.host?.ownerDocument.defaultView;
    if (view && !this.closed && URL.canParse(locator.href)) {
      const url = new URL(locator.href);
      if (linksOut.includes(url.protocol)) {
        view.open(url.href, '_blank', 'noopener,noreferrer');
      }
    }
    return true;
  }

  private moved(locator: Locator): void {
    this.locator = locator;
    this.report();
  }

  // The chapter can change after the position it is reported for, so the place is reported again with its title.
  private chapterChanged(item: TimelineItem | undefined): void {
    this.chapterTitle = item?.title?.trim() || null;
    if (this.locator) {
      this.report();
    }
  }

  private report(): void {
    const locator = this.locator;
    if (this.closed || !locator) {
      return;
    }
    const place: ReaderPlace = {
      locator: locator.serialize() as ReaderLocator,
      fraction: locator.locations?.totalProgression ?? 0,
      chapterTitle: this.chapterTitle ?? (locator.title?.trim() || null),
    };
    this.options?.onPlace(place);
  }

  /**
   * A tap or click inside a chapter frame that was not on a link: the navigator follows links before this is called.
   * Returning true stops the navigator's own handling, which turns the page on the outer quarters.
   */
  private tappedInFrame(event: FrameClickEvent): boolean {
    const frame = this.book?.navigator._cframes[0];
    const width = frame?.iframe.contentWindow?.innerWidth;
    if (width) {
      // The frame reports its clientX multiplied by the device pixel ratio.
      const ratio = this.host?.ownerDocument.defaultView?.devicePixelRatio || 1;
      this.tapped(event.x / ratio / width);
    }
    return true;
  }

  /** A click beside the book's frames, in the space around the text. */
  private readonly onMarginClick = (event: MouseEvent): void => {
    const host = this.host;
    if (!host || !this.opened) {
      return;
    }
    const rect = host.getBoundingClientRect();
    if (rect.width > 0) {
      this.tapped((event.clientX - rect.left) / rect.width);
    }
  };

  private tapped(across: number): void {
    if (this.closed) {
      return;
    }
    const zone: ReaderTapZone = across < 1 / 3 ? 'left' : across > 2 / 3 ? 'right' : 'middle';
    this.options?.onTap(zone);
  }

  private keyPressed(data: KeyboardPeripheralEventData): void {
    if (this.closed || data.altKey || data.ctrlKey || data.metaKey) {
      return;
    }
    if (data.type === keyLeft) {
      this.options?.onKey('left');
    } else if (data.type === keyRight) {
      this.options?.onKey('right');
    }
  }

  /**
   * Runs a move once the settings change in progress, if any, is done. A layout change rebuilds the chapter's frame,
   * and a move made meanwhile would be sent to the frame being taken down: it would never finish, and the navigator
   * would then refuse to go back to the place held before the change.
   */
  private afterSettings(move: () => Promise<void>): Promise<void> {
    return this.applying.then(move);
  }

  /** One step forward or back: about a screen in scrolled layout, and otherwise the navigator's own page turn. */
  private step(
    direction: 1 | -1,
    turn: (navigator: EpubNavigator, done: () => void) => void,
  ): Promise<void> {
    return this.settings?.layout === 'scrolled' ? this.scrollStep(direction) : this.move(turn);
  }

  /**
   * In scrolled layout the navigator's own next and previous change chapter straight away, so the chapter's frame is
   * scrolled from here by about a screen instead, and the chapter changes only at its end or start. The frame is on
   * this origin, so its document can be reached; the navigator sees the scroll and reports the new place itself.
   */
  private scrollStep(direction: 1 | -1): Promise<void> {
    const scroller = this.book?.navigator._cframes[0]?.iframe.contentDocument?.scrollingElement;
    if (scroller) {
      const end = scroller.scrollHeight - scroller.clientHeight;
      const at = scroller.scrollTop;
      const step = Math.max(1, Math.round(scroller.clientHeight * scrollStepOfScreen));
      if (direction > 0 && at < end - scrollEndSlack) {
        scroller.scrollTop = Math.min(end, at + step);
        return Promise.resolve();
      }
      if (direction < 0 && at > scrollEndSlack) {
        scroller.scrollTop = Math.max(0, at - step);
        return Promise.resolve();
      }
    }
    return this.move((navigator, done) =>
      direction > 0 ? navigator.goForward(false, done) : navigator.goBackward(false, done),
    );
  }

  /** Runs one navigator move and resolves when it is done, whether or not it moved (at the book's end, it does not). */
  private move(step: (navigator: EpubNavigator, done: () => void) => void): Promise<void> {
    const navigator = this.book?.navigator;
    if (!navigator) {
      return Promise.resolve();
    }
    return new Promise((resolve, reject) => {
      try {
        step(navigator, () => resolve());
      } catch (error) {
        reject(error);
      }
    });
  }

  private fail(error: unknown): void {
    if (this.closed) {
      return;
    }
    if (this.opened) {
      this.options?.onError(error);
    } else if (this.failure === undefined) {
      this.failure = error ?? new Error('The book failed while opening');
    }
  }

  // `close` was called while the book was still opening.
  private ensureOpen(): void {
    if (this.closed) {
      throw new Error('The reader was closed while the book was opening');
    }
  }
}

// Everything but the preferences, which come from the settings the book is opened with.
const configuration: EpubNavigatorConfiguration = {
  preferences: {},
  defaults: {},
  // The default, 'always', writes upgrade-insecure-requests into every frame's policy, which breaks a chapter's
  // stylesheets and images on plain http. 'auto' upgrades only a publication served over https.
  upgradeInsecureRequests: 'auto',
  keyboardPeripherals: [
    {
      type: keyLeft,
      keyCombos: [{ keyCode: arrowLeftKeyCode, suppressOnInteractiveElement: textEntry }],
    },
    {
      type: keyRight,
      keyCombos: [{ keyCode: arrowRightKeyCode, suppressOnInteractiveElement: textEntry }],
    },
  ],
};

/** The preferences that show a book with the given settings. */
function preferencesFor(settings: ReaderSettings): IEpubPreferences {
  return {
    ...layoutPreferences(settings),
    ...fontSizePreferences(settings),
    ...themePreferences(settings),
  };
}

function layoutPreferences(settings: ReaderSettings): IEpubPreferences {
  return { scroll: settings.layout === 'scrolled' };
}

// The navigator takes font size as a fraction of the book's own, and ignores one outside 0.7 to 4.
function fontSizePreferences(settings: ReaderSettings): IEpubPreferences {
  return { fontSize: settings.fontSize / 100 };
}

function themePreferences(settings: ReaderSettings): IEpubPreferences {
  // Only the colours change: images are left as they are.
  const colours = readerThemeColours[settings.theme];
  return {
    backgroundColor: colours.background,
    textColor: colours.text,
    linkColor: colours.link,
    visitedColor: colours.link,
  };
}

/** The saved place, when the book has the file it points into; otherwise the book opens at its start. */
function startingPlace(
  publication: Publication,
  place: ReaderLocator | undefined,
): Locator | undefined {
  if (!place) {
    return undefined;
  }
  let locator: Locator | undefined;
  try {
    locator = Locator.deserialize(place);
  } catch {
    return undefined;
  }
  // The navigator, given a place whose file is not in the reading order, shows nothing at all.
  return locator && publication.readingOrder.findWithHref(locator.href) ? locator : undefined;
}

/** The manifest's `self` link, when it is an absolute URL. */
function absoluteSelfLink(manifest: unknown): string | undefined {
  if (typeof manifest !== 'object' || manifest === null) {
    return undefined;
  }
  const links: unknown = (manifest as { links?: unknown }).links;
  if (!Array.isArray(links)) {
    return undefined;
  }
  for (const link of links as { rel?: unknown; href?: unknown }[]) {
    const rel = link?.rel;
    const isSelf = rel === 'self' || (Array.isArray(rel) && rel.includes('self'));
    if (isSelf && typeof link.href === 'string' && URL.canParse(link.href)) {
      return link.href;
    }
  }
  return undefined;
}

function contentsOf(links: readonly Link[]): ReaderContentsEntry[] {
  return links.map((link) => ({
    label: (link.title ?? '').trim(),
    href: link.href,
    children: contentsOf(link.children?.items ?? []),
  }));
}

function urlOf(input: RequestInfo | URL): string {
  return typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
}
