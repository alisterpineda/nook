import { ParamMap } from '@angular/router';

/** The value of the URL's `section` query parameter that stands for the Default area, the works in no section. */
export const defaultArea = 'default';

/** What a URL's `section` query parameter names: every work, the Default area, or one section. */
export type LibraryArea = { kind: 'all' } | { kind: 'default' } | { kind: 'section'; id: string };

/** Reads the area a URL names from its `section` query parameter. */
export function readArea(params: ParamMap): LibraryArea {
  const value = params.get('section');
  if (value === null) {
    return { kind: 'all' };
  }
  return value === defaultArea ? { kind: 'default' } : { kind: 'section', id: value };
}

/** The `section` query parameter that names `area`, or `null` when it is every work and needs none. */
export function areaParam(area: LibraryArea): string | null {
  switch (area.kind) {
    case 'all':
      return null;
    case 'default':
      return defaultArea;
    case 'section':
      return area.id;
  }
}

export function sameArea(one: LibraryArea, other: LibraryArea): boolean {
  return areaParam(one) === areaParam(other);
}
