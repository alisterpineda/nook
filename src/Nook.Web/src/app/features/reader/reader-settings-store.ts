import { DOCUMENT, Injectable, inject } from '@angular/core';
import {
  ReaderSettings,
  ReaderTheme,
  readerFonts,
  readerLayouts,
  readerLineHeights,
  readerMargins,
  readerThemes,
} from './reader-engine';
import { readStoredJson, writeStoredJson } from './reader-storage';

/** Where the settings are kept: one entry for every book. */
const key = 'nook.reader.settings';

/** The theme chosen for books: one of the reader's own, or whichever of light and dark the app is in. */
export type ReaderThemeChoice = ReaderTheme | 'auto';

/** Every theme choice, in the order the reader offers them. */
export const readerThemeChoices: readonly ReaderThemeChoice[] = ['auto', ...readerThemes];

/** How books are shown, as chosen and kept: what the engine is given, but with a theme that may follow the app's. */
export type KeptReaderSettings = Omit<ReaderSettings, 'theme'> & {
  readonly theme: ReaderThemeChoice;
};

/** The smallest and largest font size, and the step between sizes, as percentages of the book's own. */
export const readerFontSize = { min: 70, max: 250, step: 10 } as const;

const defaultFontSize = 100;

/**
 * Keeps how books are shown (layout, font size, theme, font, line height and margins) in the browser's storage under
 * `nook.reader.settings`, the same for every book. A setting that is not kept, or not one the reader knows, starts
 * paginated, at 100%, in the app's light or dark, in the book's own font and line height, and with normal margins.
 * A font size is kept between 70% and 250%, on a step of 10%. Storage that fails (private mode, a full quota) is
 * ignored: the reader then starts from those defaults.
 */
@Injectable({ providedIn: 'root' })
export class ReaderSettingsStore {
  private readonly document = inject(DOCUMENT);

  /** The kept settings, each one that is missing or unknown replaced by its default. */
  read(): KeptReaderSettings {
    const kept = this.kept();
    return {
      layout: readerLayouts.find((layout) => layout === kept['layout']) ?? 'paginated',
      fontSize:
        typeof kept['fontSize'] === 'number' ? fontSizeWithin(kept['fontSize']) : defaultFontSize,
      theme: readerThemeChoices.find((theme) => theme === kept['theme']) ?? 'auto',
      font: readerFonts.find((font) => font === kept['font']) ?? 'book',
      lineHeight: readerLineHeights.find((height) => height === kept['lineHeight']) ?? 'book',
      margins: readerMargins.find((margins) => margins === kept['margins']) ?? 'normal',
    };
  }

  /** Keeps `settings` for every book opened from now on, its font size brought within the limits. */
  save(settings: KeptReaderSettings): void {
    const kept: KeptReaderSettings = {
      layout: settings.layout,
      fontSize: fontSizeWithin(settings.fontSize),
      theme: settings.theme,
      font: settings.font,
      lineHeight: settings.lineHeight,
      margins: settings.margins,
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
}

/** `size` rounded to the nearest step and held between the smallest and largest size; 100 when it is not finite. */
function fontSizeWithin(size: number): number {
  if (!Number.isFinite(size)) {
    return defaultFontSize;
  }
  const stepped = Math.round(size / readerFontSize.step) * readerFontSize.step;
  return Math.min(readerFontSize.max, Math.max(readerFontSize.min, stepped));
}
