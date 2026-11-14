import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CoverSize } from '../../api/works-api';
import { text } from '../../testing/element-text';
import { WorkCover, WorkCoverSubject } from './work-cover';

describe('WorkCover', () => {
  const dune: WorkCoverSubject = {
    id: '0198a1b2-0000-7000-8000-000000000001',
    title: 'Dune',
    coverHash: 'abc123',
  };

  let fixture: ComponentFixture<WorkCover>;
  let cover: HTMLElement;

  async function render(work: WorkCoverSubject, size: CoverSize): Promise<void> {
    fixture.componentRef.setInput('work', work);
    fixture.componentRef.setInput('size', size);
    await fixture.whenStable();
  }

  beforeEach(() => {
    fixture = TestBed.createComponent(WorkCover);
    cover = fixture.nativeElement;
  });

  it.each<CoverSize>(['small', 'large'])(
    'shows the cover image at the size asked for, with the cover hash in its URL (%s)',
    async (size) => {
      await render(dune, size);

      const image = cover.querySelector('img');
      expect(image?.getAttribute('src')).toBe(`/api/works/${dune.id}/cover?size=${size}&v=abc123`);
      expect(image?.getAttribute('alt')).toBe('');
      expect(text(cover)).toBe('');
    },
  );

  it('shows a placeholder with the title when the work has no cover', async () => {
    await render({ ...dune, coverHash: null }, 'small');

    expect(cover.querySelector('img')).toBeNull();
    expect(text(cover)).toBe('Dune');
  });

  it('shows the placeholder when the image fails to load', async () => {
    await render(dune, 'small');

    cover.querySelector('img')!.dispatchEvent(new Event('error'));
    await fixture.whenStable();

    expect(cover.querySelector('img')).toBeNull();
    expect(text(cover)).toBe('Dune');
  });

  it('tries the image again when it is given another work', async () => {
    await render(dune, 'small');
    cover.querySelector('img')!.dispatchEvent(new Event('error'));
    await fixture.whenStable();

    const goodOmens: WorkCoverSubject = {
      id: '0198a1b2-0000-7000-8000-000000000002',
      title: 'Good Omens',
      coverHash: 'def456',
    };
    await render(goodOmens, 'small');

    expect(cover.querySelector('img')?.getAttribute('src')).toBe(
      `/api/works/${goodOmens.id}/cover?size=small&v=def456`,
    );
  });
});
