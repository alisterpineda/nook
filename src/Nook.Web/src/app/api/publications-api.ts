/**
 * Where an item's book is described as a Readium publication. The manifest's own `self` link is where the rest of the
 * publication is fetched from, so this is the only publication URL the app builds.
 */
export function publicationManifestUrl(itemId: string): string {
  return `/api/items/${encodeURIComponent(itemId)}/publication/manifest.json`;
}
