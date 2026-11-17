import { components } from '../api/schema';

export type ContentAccess = components['schemas']['ContentAccess'];

/** The levels from least to most: each one gives everything the ones before it give. */
const levels: readonly ContentAccess[] = ['none', 'browse', 'read', 'download'];

/** Whether a caller whose level is `access` may do what needs the level `needed`. */
export function allows(access: ContentAccess, needed: ContentAccess): boolean {
  return levels.indexOf(access) >= levels.indexOf(needed);
}
