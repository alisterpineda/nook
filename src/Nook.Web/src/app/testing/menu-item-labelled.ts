import { text } from './element-text';

/**
 * The item of an open menu whose text is `label`, for specs. A menu is shown over the page, so it is looked for in
 * the whole document. Throws when there is none, as while the menu is still opening.
 */
export function menuItemLabelled(label: string): HTMLElement {
  const found = Array.from(document.querySelectorAll<HTMLElement>('[role="menuitem"]')).find(
    (candidate) => text(candidate) === label,
  );
  if (!found) {
    throw new Error(`No menu item labelled ${label}`);
  }
  return found;
}
