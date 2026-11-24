import { Params } from '@angular/router';
import { SearchHit, SearchSource } from '../api/search-api';
import { termParam } from '../core/search-term';

/** How the palette shows and opens what each kind of search hit finds. */
export interface SearchSourceView {
  /** The group's label, and the name in its "View all" row. */
  label: string;
  /** The name of the icon that leads each hit. */
  icon: string;
  /** The router commands of the page a hit opens. */
  open(hit: SearchHit): unknown[];
  /** Where all the results for a term are listed, for a source whose list can take a term. */
  viewAll?(term: string): { commands: unknown[]; queryParams: Params };
}

export const searchSources: Record<SearchSource, SearchSourceView> = {
  works: {
    label: 'Works',
    icon: 'lucideBook',
    open: (hit) => ['/works', hit.id],
    viewAll: (term) => ({ commands: ['/library'], queryParams: { [termParam]: term } }),
  },
};
