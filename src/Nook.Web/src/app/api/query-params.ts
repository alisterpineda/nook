import { HttpParams } from '@angular/common/http';

/** The query parameters for a request: each member of `query` that is defined, under its own name, and a list once for each of its members. */
export function toParams(
  query: Record<string, string | number | boolean | readonly string[] | undefined>,
): HttpParams {
  let params = new HttpParams();
  for (const [name, value] of Object.entries(query)) {
    if (value !== undefined) {
      params =
        typeof value === 'object' ? params.appendAll({ [name]: value }) : params.set(name, value);
    }
  }
  return params;
}
