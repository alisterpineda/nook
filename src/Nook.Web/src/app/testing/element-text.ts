/** What an element says, for specs: its text with each run of whitespace as one space, or nothing when it is missing. */
export function text(element: Element | null | undefined): string {
  return (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
}
