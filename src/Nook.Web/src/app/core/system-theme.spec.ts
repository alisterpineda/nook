import { TestBed } from '@angular/core/testing';
import { SystemTheme } from './system-theme';

describe('SystemTheme', () => {
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

  afterEach(() => {
    window.matchMedia = originalMatchMedia;
    document.documentElement.classList.remove('dark');
  });

  it('adds the dark class when the system is dark', () => {
    stubSystemSetting(true);

    const theme = TestBed.inject(SystemTheme);
    expect(theme.dark()).toBe(false);
    theme.start();

    expect(window.matchMedia).toHaveBeenCalledWith('(prefers-color-scheme: dark)');
    expect(document.documentElement.classList.contains('dark')).toBe(true);
    expect(theme.dark()).toBe(true);
  });

  it('leaves the dark class off when the system is light', () => {
    stubSystemSetting(false);

    TestBed.inject(SystemTheme).start();

    expect(document.documentElement.classList.contains('dark')).toBe(false);
  });

  it('follows a change of the system setting', () => {
    stubSystemSetting(false);
    const theme = TestBed.inject(SystemTheme);
    theme.start();

    listeners.forEach((listener) => listener({ matches: true }));
    expect(document.documentElement.classList.contains('dark')).toBe(true);
    expect(theme.dark()).toBe(true);

    listeners.forEach((listener) => listener({ matches: false }));
    expect(document.documentElement.classList.contains('dark')).toBe(false);
    expect(theme.dark()).toBe(false);
  });
});
