import { components } from '../../api/schema';
import { formatLabel, roleLabel, seriesText } from './work-labels';

type ContributorRole = components['schemas']['ContributorRole'];
type ItemFormat = components['schemas']['ItemFormat'];

describe('work labels', () => {
  it('labels the formats it knows and shows any other as the API sent it', () => {
    expect(formatLabel('epub')).toBe('EPUB');
    expect(formatLabel('m4b' as ItemFormat)).toBe('m4b');
  });

  it('labels the roles it knows and shows any other as the API sent it', () => {
    expect(roleLabel('author')).toBe('Author');
    expect(roleLabel('narrator')).toBe('Narrator');
    expect(roleLabel('translator')).toBe('Translator');
    expect(roleLabel('editor')).toBe('Editor');
    expect(roleLabel('illustrator' as ContributorRole)).toBe('illustrator');
  });

  it('shows a series with its position when it has one', () => {
    const id = '0198a1b2-0000-7000-8000-0000000000c1';
    expect(seriesText({ id, name: 'Dune', position: 1 })).toBe('Dune #1');
    expect(seriesText({ id, name: 'Discworld', position: 2.5 })).toBe('Discworld #2.5');
    expect(seriesText({ id, name: 'Mars', position: null })).toBe('Mars');
    expect(seriesText(null)).toBe('');
  });
});
