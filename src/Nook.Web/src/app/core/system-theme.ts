import { DOCUMENT, Injectable, inject } from '@angular/core';

/**
 * Keeps the `dark` class on the root element in step with the system's light or dark setting. The theme in
 * `styles.css` holds its dark colours under `:root.dark`, so the class is what switches them on.
 */
@Injectable({ providedIn: 'root' })
export class SystemTheme {
  private readonly document = inject(DOCUMENT);

  start(): void {
    const view = this.document.defaultView;
    if (!view) {
      return;
    }

    const systemIsDark = view.matchMedia('(prefers-color-scheme: dark)');
    this.apply(systemIsDark.matches);
    systemIsDark.addEventListener('change', (event) => this.apply(event.matches));
  }

  private apply(dark: boolean): void {
    this.document.documentElement.classList.toggle('dark', dark);
  }
}
