import { TestBed } from '@angular/core/testing';
import { PageName } from './page-name';

describe('PageName', () => {
  it('has no name for a page that has not said one', () => {
    expect(TestBed.inject(PageName).of('a')).toBeNull();
  });

  it('keeps the name of the page on show when an earlier page names itself late', () => {
    const names = TestBed.inject(PageName);

    names.set('b', 'Emma');
    names.set('a', 'Dune');

    expect(names.of('b')).toBe('Emma');
  });
});
