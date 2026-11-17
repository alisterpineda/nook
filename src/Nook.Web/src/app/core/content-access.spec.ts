import { ContentAccess, allows } from './content-access';

describe('allows', () => {
  const levels: ContentAccess[] = ['none', 'browse', 'read', 'download'];

  it('is true for the level needed and every level above it', () => {
    for (const [held, access] of levels.entries()) {
      for (const [asked, needed] of levels.entries()) {
        expect(allows(access, needed), `${access} for ${needed}`).toBe(held >= asked);
      }
    }
  });
});
