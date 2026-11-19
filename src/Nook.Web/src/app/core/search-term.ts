import { ParamMap } from '@angular/router';

/** The query parameter of the list of works that carries the words searched for. */
export const termParam = 'q';

/** Reads the words a URL searches for from its `q` query parameter, trimmed, or `null` when it names none. */
export function readTerm(params: ParamMap): string | null {
  return params.get(termParam)?.trim() || null;
}
