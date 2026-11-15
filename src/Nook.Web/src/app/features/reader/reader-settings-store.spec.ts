import { TestBed } from '@angular/core/testing';
import { ReaderSettings } from './reader-engine';
import { ReaderSettingsStore } from './reader-settings-store';

const key = 'nook.reader.settings';

function stored(): unknown {
  const value = localStorage.getItem(key);
  return value === null ? null : JSON.parse(value);
}

describe('ReaderSettingsStore', () => {
  let store: ReaderSettingsStore;

  beforeEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove('dark');
    store = TestBed.inject(ReaderSettingsStore);
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
    localStorage.clear();
    document.documentElement.classList.remove('dark');
  });

  it("starts paginated at 100 percent in the app's light or dark", () => {
    expect(store.read()).toEqual({ layout: 'paginated', fontSize: 100, theme: 'light' });

    document.documentElement.classList.add('dark');
    expect(store.read()).toEqual({ layout: 'paginated', fontSize: 100, theme: 'dark' });
  });

  it('gives back what was saved', () => {
    const settings: ReaderSettings = { layout: 'scrolled', fontSize: 130, theme: 'sepia' };

    store.save(settings);

    expect(stored()).toEqual(settings);
    expect(TestBed.inject(ReaderSettingsStore).read()).toEqual(settings);

    document.documentElement.classList.add('dark');
    store.save({ layout: 'paginated', fontSize: 70, theme: 'light' });
    expect(store.read()).toEqual({ layout: 'paginated', fontSize: 70, theme: 'light' });
  });

  it('keeps font size between 70 and 250', () => {
    for (const [saved, read] of [
      [10, 70],
      [69, 70],
      [70, 70],
      [250, 250],
      [251, 250],
      [400, 250],
      [104, 100],
      [105, 110],
      [136, 140],
    ]) {
      localStorage.setItem(
        key,
        JSON.stringify({ layout: 'paginated', fontSize: saved, theme: 'light' }),
      );
      expect(store.read().fontSize, `saved ${saved}`).toBe(read);
    }

    store.save({ layout: 'paginated', fontSize: 300, theme: 'light' });
    expect(stored()).toEqual({ layout: 'paginated', fontSize: 250, theme: 'light' });
  });

  it('falls back to the defaults when storage holds something else or fails', () => {
    const defaults: ReaderSettings = { layout: 'paginated', fontSize: 100, theme: 'light' };
    for (const value of [
      'not json',
      'null',
      '42',
      '"scrolled"',
      '[{"layout":"scrolled"}]',
      '{}',
      '{"layout":"sideways","fontSize":"big","theme":"blue"}',
      '{"layout":"paginated","fontSize":null,"theme":"light"}',
      '{"layout":"paginated","fontSize":1e400,"theme":"light"}',
    ]) {
      localStorage.setItem(key, value);
      expect(store.read(), value).toEqual(defaults);
    }

    localStorage.setItem(key, '{"layout":"scrolled","fontSize":"big","theme":"sepia"}');
    expect(store.read()).toEqual({ layout: 'scrolled', fontSize: 100, theme: 'sepia' });

    localStorage.setItem(
      key,
      JSON.stringify({ layout: 'scrolled', fontSize: 150, theme: 'sepia' }),
    );
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('The storage is not available');
    });
    expect(store.read()).toEqual(defaults);
  });

  it('ignores a failure to write', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('The quota is used up');
    });

    expect(() => store.save({ layout: 'scrolled', fontSize: 130, theme: 'sepia' })).not.toThrow();
    expect(localStorage.getItem(key)).toBeNull();
  });
});
