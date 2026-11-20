/** The app's name, which the sidebar's header shows and every tab's title ends with. */
export const appName = 'Nook';

/** The tab's title for the page called `page`, or for one with no name of its own. */
export function tabTitle(page?: string | null): string {
  return page ? `${page} · ${appName}` : appName;
}
