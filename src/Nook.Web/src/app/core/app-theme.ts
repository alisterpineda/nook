import { DOCUMENT, Injectable, inject, signal } from '@angular/core';
import { readStoredJson, writeStoredJson } from './stored-json';

/** How the app's colours are chosen: `auto` follows the system's light or dark setting. */
export type ThemeChoice = 'auto' | 'light' | 'dark';

/** The choices, in the order they are offered, with what each is called. */
export const themeChoices: readonly { value: ThemeChoice; label: string }[] = [
  { value: 'auto', label: 'Auto' },
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
];

/** `value` as a theme choice, or undefined when it is not one. */
export function asThemeChoice(value: unknown): ThemeChoice | undefined {
  return themeChoices.find((choice) => choice.value === value)?.value;
}

const key = 'nook.theme';

/**
 * Keeps the `dark` class on the root element in step with the theme chosen: light, dark, or on auto the system's light
 * or dark setting. The theme in `styles.css` holds its dark colours under `:root.dark`, so the class is what switches
 * them on. The choice is kept in this browser. Whether the app is dark can be read from `dark`, by what has colours of
 * its own to keep in step.
 */
@Injectable({ providedIn: 'root' })
export class AppTheme {
  private readonly document = inject(DOCUMENT);
  private readonly chosen = signal<ThemeChoice>('auto');
  private readonly isDark = signal(false);
  private systemIsDark = false;

  /** The theme chosen. */
  readonly choice = this.chosen.asReadonly();

  /** Whether the app is showing its dark colours. */
  readonly dark = this.isDark.asReadonly();

  start(): void {
    this.chosen.set(asThemeChoice(readStoredJson(this.document, key)) ?? 'auto');

    const system = this.document.defaultView?.matchMedia('(prefers-color-scheme: dark)');
    if (system) {
      this.systemIsDark = system.matches;
      system.addEventListener('change', (event) => {
        this.systemIsDark = event.matches;
        this.apply();
      });
    }
    this.apply();
  }

  /** Shows the app in `choice` and keeps it for the next visit. */
  choose(choice: ThemeChoice): void {
    this.chosen.set(choice);
    writeStoredJson(this.document, key, choice);
    this.apply();
  }

  private apply(): void {
    const choice = this.chosen();
    const dark = choice === 'auto' ? this.systemIsDark : choice === 'dark';
    this.document.documentElement.classList.toggle('dark', dark);
    this.isDark.set(dark);
  }
}
