import { Component, inject } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideMonitor, lucideMoon, lucideSun } from '@ng-icons/lucide';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmToggleGroupImports } from '@spartan-ng/helm/toggle-group';
import { AppTheme, ThemeChoice, asThemeChoice, themeChoices } from '../core/app-theme';

const icons: Record<ThemeChoice, string> = {
  auto: 'lucideMonitor',
  light: 'lucideSun',
  dark: 'lucideMoon',
};

/** The dialog that chooses the app's theme. A theme is shown as it is chosen, and the dialog stays open. */
@Component({
  selector: 'app-theme-dialog',
  imports: [NgIcon, HlmDialogImports, HlmToggleGroupImports],
  providers: [provideIcons({ lucideMonitor, lucideMoon, lucideSun })],
  // The dialog spaces its own children, but this element sits between it and the parts below, so it spaces them.
  host: { class: 'grid gap-6' },
  template: `
    <hlm-dialog-header>
      <h2 hlmDialogTitle>Theme</h2>
      <p hlmDialogDescription>Choose how Nook looks in this browser.</p>
    </hlm-dialog-header>
    <div
      hlmToggleGroup
      type="single"
      variant="outline"
      spacing="2"
      aria-label="Theme"
      class="grid w-full grid-cols-3"
      [nullable]="false"
      [value]="theme.choice()"
      (valueChange)="choose($event)"
    >
      @for (choice of choices; track choice.value) {
        <button
          hlmToggleGroupItem
          type="button"
          class="h-auto flex-col gap-2 py-3 data-[state=on]:border-primary"
          [value]="choice.value"
        >
          <ng-icon [name]="icons[choice.value]" />
          {{ choice.label }}
        </button>
      }
    </div>
  `,
})
export class ThemeDialog {
  protected readonly theme = inject(AppTheme);
  protected readonly choices = themeChoices;
  protected readonly icons = icons;

  protected choose(value: unknown): void {
    const choice = asThemeChoice(value);
    if (choice) {
      this.theme.choose(choice);
    }
  }
}
