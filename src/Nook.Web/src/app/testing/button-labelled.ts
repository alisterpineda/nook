import { text } from './element-text';

/** The button inside `element` whose text is `label`, for specs. Throws when there is none. */
export function buttonLabelled(element: HTMLElement, label: string): HTMLButtonElement {
  const found = Array.from(element.querySelectorAll('button')).find(
    (candidate) => text(candidate) === label,
  );
  if (!found) {
    throw new Error(`No button labelled ${label}`);
  }
  return found;
}
