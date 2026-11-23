/** Presses Enter in `input` the way the keyboard does, so the page hears of it, for specs. */
export function pressEnter(input: HTMLInputElement): void {
  input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
}
