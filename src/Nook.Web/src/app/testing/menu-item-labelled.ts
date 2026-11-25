import { text } from './element-text';

/** The items of the open menu in order, for specs. A menu is shown over the page, so the whole document is searched. */
export function menuItems(): HTMLElement[] {
  return Array.from(document.querySelectorAll<HTMLElement>('[role="menuitem"]'));
}

/**
 * The item of the open menu whose text is `label`, for specs. Throws when there is none, as while the menu is still
 * opening.
 */
export function menuItemLabelled(label: string): HTMLElement {
  const found = menuItems().find((candidate) => text(candidate) === label);
  if (!found) {
    throw new Error(`No menu item labelled ${label}`);
  }
  return found;
}
