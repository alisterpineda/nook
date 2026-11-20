import { TestBed } from '@angular/core/testing';
import { KeptReaderSettings, ReaderSettingsStore } from './reader-settings-store';

const key = 'nook.reader.settings';

/** How a book is shown when nothing is kept, with `changed` in place of what it names. */
function settingsWith(changed: Partial<KeptReaderSettings> = {}): KeptReaderSettings {
  return {
    layout: 'paginated',
    fontSize: 100,
    theme: 'auto',
    font: 'book',
    lineHeight: 'book',
    margins: 'normal',
    ...changed,
  };
}

function stored(): unknown {
  const value = localStorage.getItem(key);
  return value === null ? null : JSON.parse(value);
}

describe('ReaderSettingsStore', () => {
  let store: ReaderSettingsStore;

  beforeEach(() => {
    localStorage.clear();
    store = TestBed.inject(ReaderSettingsStore);
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it("starts paginated at 100 percent on the app's theme, as the book sets its text", () => {
    expect(store.read()).toEqual({
      layout: 'paginated',
      fontSize: 100,
      theme: 'auto',
      font: 'book',
      lineHeight: 'book',
      margins: 'normal',
    });
  });

  it('gives back what was saved', () => {
    const settings: KeptReaderSettings = {
      layout: 'scrolled',
      fontSize: 130,
      theme: 'sepia',
      font: 'sans',
      lineHeight: 'tight',
      margins: 'wide',
    };

    store.save(settings);

    expect(stored()).toEqual(settings);
    expect(TestBed.inject(ReaderSettingsStore).read()).toEqual(settings);

    store.save(settingsWith({ fontSize: 70, theme: 'light' }));
    expect(store.read()).toEqual(settingsWith({ fontSize: 70, theme: 'light' }));
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

    store.save(settingsWith({ fontSize: 300 }));
    expect(stored()).toEqual(settingsWith({ fontSize: 250 }));
  });

  it('falls back to the defaults when storage holds something else or fails', () => {
    const defaults = settingsWith();
    for (const value of [
      'not json',
      'null',
      '42',
      '"scrolled"',
      '[{"layout":"scrolled"}]',
      '{}',
      '{"layout":"sideways","fontSize":"big","theme":"blue"}',
      '{"font":"comic","lineHeight":1.5,"margins":"none"}',
      '{"layout":"paginated","fontSize":null,"theme":"auto"}',
      '{"layout":"paginated","fontSize":1e400,"theme":"auto"}',
    ]) {
      localStorage.setItem(key, value);
      expect(store.read(), value).toEqual(defaults);
    }

    localStorage.setItem(key, '{"layout":"scrolled","fontSize":"big","theme":"sepia"}');
    expect(store.read()).toEqual(settingsWith({ layout: 'scrolled', theme: 'sepia' }));

    // Settings kept before there were fonts, line heights and margins to choose.
    localStorage.setItem(key, '{"layout":"scrolled","fontSize":150,"theme":"sepia"}');
    expect(store.read()).toEqual(
      settingsWith({ layout: 'scrolled', fontSize: 150, theme: 'sepia' }),
    );

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

    expect(() => store.save(settingsWith({ layout: 'scrolled' }))).not.toThrow();
    expect(localStorage.getItem(key)).toBeNull();
  });
});
