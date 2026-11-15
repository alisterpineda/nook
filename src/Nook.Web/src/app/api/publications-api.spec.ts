import { publicationManifestUrl } from './publications-api';

describe('publication URLs', () => {
  it('publicationManifestUrl names the item', () => {
    expect(publicationManifestUrl('0198a1b2-0000-7000-8000-0000000000e1')).toBe(
      '/api/items/0198a1b2-0000-7000-8000-0000000000e1/publication/manifest.json',
    );
  });

  it('publicationManifestUrl encodes the item id', () => {
    expect(publicationManifestUrl('item/1?x')).toBe(
      '/api/items/item%2F1%3Fx/publication/manifest.json',
    );
  });
});
