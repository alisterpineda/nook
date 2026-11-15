import { DOCUMENT, Injectable, inject } from '@angular/core';
import { ReaderLayout, ReaderSettings, ReaderTheme, readerThemes } from './reader-engine';
import { readStoredJson, writeStoredJson } from './reader-storage';

/** Where the settings are kept: one entry for every book. */
const key = 'nook.reader.settings';

/** The smallest and largest font size, and the step between sizes, as percentages of the book's own. */
export const readerFontSize = { min: 70, max: 250, step: 10 } as const;

const defaultFontSize = 100;
const layouts: readonly ReaderLayout[] = ['paginated', 'scrolled'];

/**
 * Keeps how books are shown (layout, font size and theme) in the browser's storage under `nook.reader.settings`, the
 * same for every book. A setting that is not kept, or not one the reader knows, starts paginated, at 100%, and in
 * dark when the app is dark and light otherwise. A font size is kept between 70% and 250%, on a step of 10%. Storage
 * that fails (private mode, a full quota) is ignored: the reader then starts from those defaults.
 */
@Injectable({ providedIn: 'root' })
export class ReaderSettingsStore {
  private readonly document = inject(DOCUMENT);

  /** The kept settings, each one that is missing or unknown replaced by its default. */
  read(): ReaderSettings {
    const kept = this.kept();
    return {
      layout: layouts.find((layout) => layout === kept['layout']) ?? 'paginated',
      fontSize:
        typeof kept['fontSize'] === 'number' ? fontSizeWithin(kept['fontSize']) : defaultFontSize,
      theme: readerThemes.find((theme) => theme === kept['theme']) ?? this.appTheme(),
    };
  }

  /** Keeps `settings` for every book opened from now on, its font size brought within the limits. */
  save(settings: ReaderSettings): void {
    const kept: ReaderSettings = {
      layout: settings.layout,
      fontSize: fontSizeWithin(settings.fontSize),
      theme: settings.theme,
    };
    // When storage fails the settings are not kept, and the next book opens with the defaults.
    writeStoredJson(this.document, key, kept);
  }

  // What storage holds, as an object, or an empty one when it holds nothing usable or cannot be read.
  private kept(): Record<string, unknown> {
    const kept = readStoredJson(this.document, key);
    return typeof kept === 'object' && kept !== null && !Array.isArray(kept)
      ? (kept as Record<string, unknown>)
      : {};
  }

  // The app shows its dark colours while the root element has the `dark` class.
  private appTheme(): ReaderTheme {
    return this.document.documentElement.classList.contains('dark') ? 'dark' : 'light';
  }
}

/** `size` rounded to the nearest step and held between the smallest and largest size; 100 when it is not finite. */
function fontSizeWithin(size: number): number {
  if (!Number.isFinite(size)) {
    return defaultFontSize;
  }
  const stepped = Math.round(size / readerFontSize.step) * readerFontSize.step;
  return Math.min(readerFontSize.max, Math.max(readerFontSize.min, stepped));
}
