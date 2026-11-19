/** Sets `input` to `value` the way typing does, so the page hears of it, for specs. */
export function typeInto(input: HTMLInputElement, value: string): void {
  input.value = value;
  input.dispatchEvent(new Event('input'));
}
