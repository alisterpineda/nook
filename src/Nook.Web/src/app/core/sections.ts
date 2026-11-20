import { Injectable, inject, signal } from '@angular/core';
import { SectionsApi } from '../api/sections-api';
import { components } from '../api/schema';
import { LibraryArea } from './library-area';

type SectionResponse = components['schemas']['SectionResponse'];

/** What every work together is called. */
export const libraryName = 'Library';

/** What the Default area, the works in no section, is called. */
export const defaultAreaName = 'Default';

/**
 * The sections the caller can see, kept where both the sidebar and the list of works can read them. The shell loads
 * them, so the list reads whatever the shell last learnt. A page that must know whether they loaded asks the API itself.
 */
@Injectable({ providedIn: 'root' })
export class Sections {
  private readonly api = inject(SectionsApi);
  private readonly listed = signal<SectionResponse[]>([]);

  /** How many times the sections have been asked for, which tells the latest answer from one that is late. */
  private asked = 0;

  /** The sections, ordered by name, and none until they are loaded or when they cannot be. */
  readonly list = this.listed.asReadonly();

  /** Asks the API for the sections, forgetting the ones known before: they may have been another user's. */
  async load(): Promise<void> {
    const asked = ++this.asked;
    this.listed.set([]);
    try {
      const sections = await this.api.list();
      // An answer to an earlier call may be for the user who was signed in then, so only the latest call's is kept.
      if (asked === this.asked) {
        this.listed.set(sections);
      }
    } catch {
      // Without the sections the sidebar still offers "Library", which is enough to keep going.
    }
  }

  /** What to call `area`, or `null` for a section whose name is not known. */
  nameOf(area: LibraryArea): string | null {
    switch (area.kind) {
      case 'all':
        return libraryName;
      case 'default':
        return defaultAreaName;
      case 'section':
        return this.listed().find((section) => section.id === area.id)?.name ?? null;
    }
  }
}
