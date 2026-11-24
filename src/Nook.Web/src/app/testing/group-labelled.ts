/** The group inside `element` whose `aria-label` is `label`, for specs. Throws when there is none. */
export function groupLabelled(element: HTMLElement, label: string): HTMLElement {
  const found = Array.from(element.querySelectorAll<HTMLElement>('[role="group"]')).find(
    (candidate) => candidate.getAttribute('aria-label') === label,
  );
  if (!found) {
    throw new Error(`No group labelled ${label}`);
  }
  return found;
}
