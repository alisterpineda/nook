import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { components } from '../../api/schema';
import { text } from '../../testing/element-text';
import { WorkCard } from './work-card';

type WorkSummaryResponse = components['schemas']['WorkSummaryResponse'];

describe('WorkCard', () => {
  const goodOmens: WorkSummaryResponse = {
    id: '0198a1b2-0000-7000-8000-000000000002',
    sectionId: null,
    title: 'Good Omens',
    authors: ['Terry Pratchett', 'Neil Gaiman'],
    series: { id: '0198a1b2-0000-7000-8000-0000000000c1', name: 'Discworld', position: 3 },
    coverHash: 'abc123',
    formats: ['epub'],
    addedAt: '2026-11-02T12:00:00Z',
  };

  let fixture: ComponentFixture<WorkCard>;
  let card: HTMLElement;

  async function render(work: WorkSummaryResponse): Promise<void> {
    fixture.componentRef.setInput('work', work);
    await fixture.whenStable();
  }

  /** What the card says beneath its cover: the title, then the authors. */
  function lines(): string[] {
    return Array.from(card.querySelectorAll('app-work-cover ~ *')).map(text);
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    fixture = TestBed.createComponent(WorkCard);
    card = fixture.nativeElement;
  });

  it('is one link to the work that holds the cover, the title and the authors', async () => {
    await render(goodOmens);

    const links = card.querySelectorAll('a');
    expect(links).toHaveLength(1);
    const link = links[0];
    expect(link.getAttribute('href')).toBe(`/works/${goodOmens.id}`);
    expect(link.querySelector('app-work-cover')).not.toBeNull();
    expect(lines()).toEqual(['Good Omens', 'Terry Pratchett, Neil Gaiman']);
    expect(Array.from(link.querySelectorAll('app-work-cover ~ *')).map(text)).toEqual(lines());
  });

  it('shows no authors line text for a work with none', async () => {
    await render({ ...goodOmens, authors: [] });

    expect(lines()).toEqual(['Good Omens', '']);
  });
});
