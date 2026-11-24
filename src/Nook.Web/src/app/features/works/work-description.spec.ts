import { ComponentFixture, TestBed } from '@angular/core/testing';
import { text } from '../../testing/element-text';
import { WorkDescription } from './work-description';

describe('WorkDescription', () => {
  let fixture: ComponentFixture<WorkDescription>;
  let description: HTMLElement;

  /** Stands in for layout, which the test DOM does not do: the text takes this much height and its box that much. */
  function layOut(textHeight: number, boxHeight: number): void {
    vi.spyOn(Element.prototype, 'scrollHeight', 'get').mockReturnValue(textHeight);
    vi.spyOn(Element.prototype, 'clientHeight', 'get').mockReturnValue(boxHeight);
  }

  async function render(value: string): Promise<void> {
    fixture.componentRef.setInput('text', value);
    await fixture.whenStable();
  }

  function paragraph(): HTMLElement {
    return description.querySelector('p')!;
  }

  function button(): HTMLButtonElement | null {
    return description.querySelector('button');
  }

  beforeEach(() => {
    fixture = TestBed.createComponent(WorkDescription);
    description = fixture.nativeElement;
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('shows the text as written, with no button, when it fits', async () => {
    layOut(60, 60);

    await render('A desert planet.\nA spice.');

    expect(paragraph().textContent).toBe('A desert planet.\nA spice.');
    expect(button()).toBeNull();
  });

  it('cuts a text that does not fit and offers the rest', async () => {
    layOut(400, 120);

    await render('A long description.');

    expect(paragraph().classList).toContain('line-clamp-6');
    expect(text(button())).toBe('Show more');
    expect(button()?.getAttribute('aria-expanded')).toBe('false');
  });

  it('shows the rest in place, and cuts it again', async () => {
    layOut(400, 120);
    await render('A long description.');

    button()!.click();
    // Shown whole, the text is as tall as its box.
    layOut(400, 400);
    await fixture.whenStable();

    expect(paragraph().classList).not.toContain('line-clamp-6');
    expect(paragraph().textContent).toBe('A long description.');
    expect(text(button())).toBe('Show less');
    expect(button()?.getAttribute('aria-expanded')).toBe('true');

    button()!.click();
    layOut(400, 120);
    await fixture.whenStable();

    expect(paragraph().classList).toContain('line-clamp-6');
    expect(text(button())).toBe('Show more');
  });

  it('cuts the next description again after one was shown whole', async () => {
    layOut(400, 120);
    await render('A long description.');
    button()!.click();
    await fixture.whenStable();

    await render('Another long description.');

    expect(paragraph().classList).toContain('line-clamp-6');
    expect(text(button())).toBe('Show more');
  });

  it('drops the button when the next description fits', async () => {
    layOut(400, 120);
    await render('A long description.');

    layOut(60, 60);
    await render('A short one.');

    expect(button()).toBeNull();
  });
});
