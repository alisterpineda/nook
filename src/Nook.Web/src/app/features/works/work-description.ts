import {
  Component,
  ElementRef,
  afterRenderEffect,
  input,
  linkedSignal,
  signal,
  viewChild,
} from '@angular/core';
import { HlmButtonImports } from '@spartan-ng/helm/button';

/**
 * A work's description, cut to its first lines when it runs longer, with a button that shows the rest in place. The
 * whole text stays in the page either way, so a search of the page and a screen reader still reach it.
 */
@Component({
  selector: 'app-work-description',
  imports: [HlmButtonImports],
  host: { class: 'block' },
  template: `
    <!-- Bound rather than written between the tags, where the template's own line breaks would join the text. -->
    <p
      #paragraph
      class="text-sm whitespace-pre-line"
      [class]="expanded() ? '' : 'line-clamp-6 sm:line-clamp-8'"
      [textContent]="text()"
    ></p>
    @if (cut()) {
      <button
        hlmBtn
        variant="link"
        size="sm"
        class="mt-1 h-auto px-0"
        type="button"
        [attr.aria-expanded]="expanded()"
        (click)="expanded.set(!expanded())"
      >
        {{ expanded() ? 'Show less' : 'Show more' }}
      </button>
    }
  `,
})
export class WorkDescription {
  readonly text = input.required<string>();

  // Going from one work to another keeps this component, and the next description starts cut again.
  protected readonly expanded = linkedSignal({ source: this.text, computation: () => false });

  /** Whether the text is longer than the lines it is cut to, which is when there is a rest to show. */
  protected readonly cut = signal(false);

  private readonly paragraph = viewChild.required<ElementRef<HTMLElement>>('paragraph');

  constructor() {
    // A new text can fill the same box as the last one, which the observer below would not report.
    afterRenderEffect(() => {
      this.text();
      this.expanded();
      this.measure();
    });

    // How many lines the text takes depends on the width, which changes with the window.
    afterRenderEffect((onCleanup) => {
      if (typeof ResizeObserver === 'undefined') {
        return;
      }
      const observer = new ResizeObserver(() => this.measure());
      observer.observe(this.paragraph().nativeElement);
      onCleanup(() => observer.disconnect());
    });
  }

  private measure(): void {
    // Shown whole, the text hides nothing to measure, and the button has to stay to cut it again.
    if (this.expanded()) {
      return;
    }
    const paragraph = this.paragraph().nativeElement;
    this.cut.set(paragraph.scrollHeight > paragraph.clientHeight);
  }
}
