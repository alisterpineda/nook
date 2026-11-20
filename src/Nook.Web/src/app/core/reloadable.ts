import { HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { BehaviorSubject, Observable, from, startWith, switchMap } from 'rxjs';

/** What a page shows while what it asked for is on its way. */
interface Loading {
  status: 'loading';
}

/**
 * Loads what each query asks for, and again each time `reload` is called, so a page can offer to retry what failed.
 * `state$` says "loading" at once for every query and every reload, then what `load` answered. An answer for a query
 * the page has already left is dropped.
 */
export function reloadable<Query, State>(
  query$: Observable<Query>,
  load: (query: Query) => Promise<State>,
): { state$: Observable<State | Loading>; reload: () => void } {
  const reloads$ = new BehaviorSubject<void>(undefined);
  return {
    state$: reloads$.pipe(
      switchMap(() => query$),
      switchMap((query) =>
        from(load(query)).pipe(startWith<State | Loading>({ status: 'loading' })),
      ),
    ),
    reload: () => reloads$.next(),
  };
}

/** How a load that threw failed: what was asked for is not there, or something else went wrong. */
export function failureOf(error: unknown): 'not-found' | 'failed' {
  return error instanceof HttpErrorResponse && error.status === HttpStatusCode.NotFound
    ? 'not-found'
    : 'failed';
}
