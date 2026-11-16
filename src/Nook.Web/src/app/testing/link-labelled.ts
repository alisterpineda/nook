import { text } from './element-text';

/** The link inside `element` whose text is `label`, for specs. Throws when there is none. */
export function linkLabelled(element: HTMLElement, label: string): HTMLAnchorElement {
  const found = Array.from(element.querySelectorAll('a')).find(
    (candidate) => text(candidate) === label,
  );
  if (!found) {
    throw new Error(`No link labelled ${label}`);
  }
  return found;
}
