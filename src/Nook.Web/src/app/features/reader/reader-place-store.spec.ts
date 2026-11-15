import { TestBed } from '@angular/core/testing';
import { ReaderPlace } from './reader-engine';
import { ReaderPlaceStore } from './reader-place-store';

const itemId = '0198a1b2-0000-7000-8000-0000000000f1';
const otherItemId = '0198a1b2-0000-7000-8000-0000000000f2';
const key = `nook.reader.position.${itemId}`;

function place(href: string, progression: number): ReaderPlace {
  return {
    locator: {
      href,
      type: 'application/xhtml+xml',
      locations: { progression, totalProgression: progression / 2 },
    },
    fraction: progression / 2,
    chapterTitle: 'Chapter 1',
  };
}

function stored(): unknown {
  const value = localStorage.getItem(key);
  return value === null ? null : JSON.parse(value);
}

describe('ReaderPlaceStore', () => {
  let store: ReaderPlaceStore;

  beforeEach(() => {
    vi.useFakeTimers();
    localStorage.clear();
    store = TestBed.inject(ReaderPlaceStore);
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
    vi.useRealTimers();
    localStorage.clear();
  });

  it("keeps the latest place's Locator, and nothing else, under the item's key", async () => {
    const first = place('OEBPS/chapter-1.xhtml', 0.1);
    const latest = place('OEBPS/chapter-2.xhtml', 0.4);

    store.save(itemId, first);
    store.save(itemId, latest);
    await vi.advanceTimersByTimeAsync(1000);

    expect(stored()).toEqual(latest.locator);
    expect(store.read(itemId)).toEqual(latest.locator);
    expect(localStorage.getItem(`nook.reader.position.${otherItemId}`)).toBeNull();
    expect(store.read(otherItemId)).toBeUndefined();
  });

  it('writes at most once a second while the place keeps changing', async () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem');
    const a = place('OEBPS/chapter-1.xhtml', 0.1);
    const b = place('OEBPS/chapter-1.xhtml', 0.2);
    const c = place('OEBPS/chapter-1.xhtml', 0.3);
    const d = place('OEBPS/chapter-1.xhtml', 0.4);

    store.save(itemId, a);
    expect(stored()).toEqual(a.locator);

    await vi.advanceTimersByTimeAsync(300);
    store.save(itemId, b);
    await vi.advanceTimersByTimeAsync(300);
    store.save(itemId, c);
    await vi.advanceTimersByTimeAsync(399);
    expect(stored()).toEqual(a.locator);

    await vi.advanceTimersByTimeAsync(1);
    expect(stored()).toEqual(c.locator);

    store.save(itemId, d);
    expect(stored()).toEqual(c.locator);
    await vi.advanceTimersByTimeAsync(1000);
    expect(stored()).toEqual(d.locator);

    await vi.advanceTimersByTimeAsync(5000);
    expect(setItem).toHaveBeenCalledTimes(3);
  });

  it('writes at once when the page is hidden', async () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem');
    const first = place('OEBPS/chapter-1.xhtml', 0.1);
    const latest = place('OEBPS/chapter-1.xhtml', 0.2);

    store.save(itemId, first);
    store.save(itemId, latest);
    expect(stored()).toEqual(first.locator);

    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
    document.dispatchEvent(new Event('visibilitychange'));

    expect(stored()).toEqual(latest.locator);
    await vi.advanceTimersByTimeAsync(5000);
    expect(setItem).toHaveBeenCalledTimes(2);
  });

  it('reads nothing when storage holds something else or fails', () => {
    for (const value of [
      'not json',
      'null',
      '"OEBPS/chapter-1.xhtml"',
      '42',
      '[{"href":"OEBPS/chapter-1.xhtml"}]',
      '{"locations":{"progression":0.5}}',
      '{"href":7}',
    ]) {
      localStorage.setItem(key, value);
      expect(store.read(itemId)).toBeUndefined();
    }

    localStorage.setItem(key, '{"href":"OEBPS/chapter-1.xhtml"}');
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('The storage is not available');
    });
    expect(store.read(itemId)).toBeUndefined();
  });

  it('ignores a failure to write', async () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('The quota is used up');
    });

    expect(() => store.save(itemId, place('OEBPS/chapter-1.xhtml', 0.1))).not.toThrow();
    store.save(itemId, place('OEBPS/chapter-1.xhtml', 0.2));
    await vi.advanceTimersByTimeAsync(1000);
    expect(localStorage.getItem(key)).toBeNull();
  });
});
