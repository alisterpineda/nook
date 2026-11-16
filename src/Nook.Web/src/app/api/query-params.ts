import { HttpParams } from '@angular/common/http';

/** The query parameters for a request: each member of `query` that is defined, under its own name. */
export function toParams(query: Record<string, string | number | boolean | undefined>): HttpParams {
  let params = new HttpParams();
  for (const [name, value] of Object.entries(query)) {
    if (value !== undefined) {
      params = params.set(name, value);
    }
  }
  return params;
}
