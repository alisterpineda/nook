import { text } from './element-text';

/**
 * The form control inside `element` that the label reading `label` names, found the way assistive technology finds
 * it, for specs. It is an input unless `kind` says otherwise. Throws when there is none of that kind.
 */
export function fieldLabelled(element: HTMLElement, label: string): HTMLInputElement;
export function fieldLabelled<T extends HTMLElement>(
  element: HTMLElement,
  label: string,
  kind: new () => T,
): T;
export function fieldLabelled(
  element: HTMLElement,
  label: string,
  kind: new () => HTMLElement = HTMLInputElement,
): HTMLElement {
  const control = Array.from(element.querySelectorAll('label')).find(
    (candidate) => text(candidate) === label,
  )?.control;
  if (!(control instanceof kind)) {
    throw new Error(`No ${kind.name} is labelled "${label}".`);
  }
  return control;
}
