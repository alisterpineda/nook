import { TestBed } from '@angular/core/testing';
import { AppTheme } from './app-theme';

describe('AppTheme', () => {
  const originalMatchMedia = window.matchMedia;
  let listeners: ((event: { matches: boolean }) => void)[];

  function stubSystemSetting(dark: boolean) {
    listeners = [];
    window.matchMedia = vi.fn((query: string) => ({
      matches: dark,
      media: query,
      addEventListener: (_type: string, listener: (event: { matches: boolean }) => void) =>
        listeners.push(listener),
    })) as unknown as typeof window.matchMedia;
  }

  function changeSystemSetting(dark: boolean) {
    listeners.forEach((listener) => listener({ matches: dark }));
  }

  function isDark(): boolean {
    return document.documentElement.classList.contains('dark');
  }

  afterEach(() => {
    window.matchMedia = originalMatchMedia;
    document.documentElement.classList.remove('dark');
    localStorage.clear();
  });

  it('adds the dark class when the system is dark', () => {
    stubSystemSetting(true);

    const theme = TestBed.inject(AppTheme);
    expect(theme.dark()).toBe(false);
    theme.start();

    expect(window.matchMedia).toHaveBeenCalledWith('(prefers-color-scheme: dark)');
    expect(theme.choice()).toBe('auto');
    expect(isDark()).toBe(true);
    expect(theme.dark()).toBe(true);
  });

  it('leaves the dark class off when the system is light', () => {
    stubSystemSetting(false);

    TestBed.inject(AppTheme).start();

    expect(isDark()).toBe(false);
  });

  it('follows a change of the system setting', () => {
    stubSystemSetting(false);
    const theme = TestBed.inject(AppTheme);
    theme.start();

    changeSystemSetting(true);
    expect(isDark()).toBe(true);
    expect(theme.dark()).toBe(true);

    changeSystemSetting(false);
    expect(isDark()).toBe(false);
    expect(theme.dark()).toBe(false);
  });

  it('shows the theme chosen whatever the system setting', () => {
    stubSystemSetting(false);
    const theme = TestBed.inject(AppTheme);
    theme.start();

    theme.choose('dark');
    expect(theme.choice()).toBe('dark');
    expect(isDark()).toBe(true);
    expect(theme.dark()).toBe(true);

    changeSystemSetting(true);
    theme.choose('light');
    expect(isDark()).toBe(false);
    expect(theme.dark()).toBe(false);

    changeSystemSetting(false);
    changeSystemSetting(true);
    expect(isDark()).toBe(false);
  });

  it('goes back to the system setting, as it is now, when auto is chosen', () => {
    stubSystemSetting(false);
    const theme = TestBed.inject(AppTheme);
    theme.start();
    theme.choose('light');
    changeSystemSetting(true);

    theme.choose('auto');

    expect(isDark()).toBe(true);
  });

  it('keeps the theme chosen for the next visit', () => {
    stubSystemSetting(false);
    TestBed.inject(AppTheme).choose('dark');

    TestBed.resetTestingModule();
    const theme = TestBed.inject(AppTheme);
    theme.start();

    expect(theme.choice()).toBe('dark');
    expect(isDark()).toBe(true);
  });

  it('is on auto when what was kept is not a theme', () => {
    stubSystemSetting(true);
    localStorage.setItem('nook.theme', '"sepia"');

    const theme = TestBed.inject(AppTheme);
    theme.start();

    expect(theme.choice()).toBe('auto');
    expect(isDark()).toBe(true);
  });
});
