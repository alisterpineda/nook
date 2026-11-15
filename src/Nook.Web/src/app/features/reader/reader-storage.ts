/**
 * What the reader keeps in the browser's storage, as JSON. Storage that fails (private mode, a full quota) is
 * ignored: nothing is read, and nothing is kept.
 */

/** The value kept under `key`, or undefined when there is none, it is not JSON, or storage cannot be read. */
export function readStoredJson(document: Document, key: string): unknown {
  try {
    const value = storageOf(document)?.getItem(key);
    return value == null ? undefined : (JSON.parse(value) as unknown);
  } catch {
    return undefined;
  }
}

/** Keeps `value` under `key`, or does nothing when storage cannot be written. */
export function writeStoredJson(document: Document, key: string, value: unknown): void {
  try {
    storageOf(document)?.setItem(key, JSON.stringify(value));
  } catch {
    // The value is not kept.
  }
}

// Reaching `localStorage` itself throws where the browser refuses storage to the page.
function storageOf(document: Document): Storage | undefined {
  return document.defaultView?.localStorage;
}
