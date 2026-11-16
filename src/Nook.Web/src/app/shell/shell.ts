import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink, RouterOutlet, isActive } from '@angular/router';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { SectionsApi } from '../api/sections-api';
import { components } from '../api/schema';
import { LibraryArea, areaParam, readArea, sameArea } from '../core/default-area';
import { Session } from '../core/session';
import { UploadQueue } from '../features/upload/upload-queue';

type SectionResponse = components['schemas']['SectionResponse'];

/**
 * The frame around every signed-in page: a collapsible sidebar with "Library", then "Default" and the user's
 * sections when there are any, then "Add books" for an admin, the user's menu in its footer, and the page itself
 * beside it.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterLink, RouterOutlet, HlmDropdownMenuImports, HlmSidebarImports],
  template: `
    <div hlmSidebarWrapper>
      <hlm-sidebar>
        <div hlmSidebarContent>
          <nav hlmSidebarGroup aria-label="Library">
            <ul hlmSidebarMenu>
              <!-- Links name only the section, so choosing one drops the page and sort of the list before. -->
              @for (link of links(); track link.section) {
                <li hlmSidebarMenuItem>
                  <a
                    hlmSidebarMenuButton
                    routerLink="/library"
                    [queryParams]="link.section === null ? null : { section: link.section }"
                    closeMobileSidebarOnClick
                    [isActive]="link.current"
                    [attr.aria-current]="link.current ? 'page' : null"
                  >
                    <span>{{ link.label }}</span>
                  </a>
                </li>
              }
            </ul>
          </nav>
          @if (isAdmin()) {
            <nav hlmSidebarGroup aria-label="Manage">
              <div hlmSidebarGroupLabel>Manage</div>
              <ul hlmSidebarMenu>
                <li hlmSidebarMenuItem>
                  <!-- Carries the section being browsed, Default included, so the upload page can preselect it. -->
                  <a
                    hlmSidebarMenuButton
                    routerLink="/upload"
                    [queryParams]="browsedSection() ? { section: browsedSection() } : null"
                    closeMobileSidebarOnClick
                    [isActive]="onUpload()"
                    [attr.aria-current]="onUpload() ? 'page' : null"
                  >
                    <span>Add books</span>
                  </a>
                </li>
              </ul>
            </nav>
          }
        </div>
        <div hlmSidebarFooter>
          @if (signOutFailed()) {
            <p class="text-destructive px-2 text-sm" role="alert">Could not sign out. Try again.</p>
          }
          <ul hlmSidebarMenu>
            <li hlmSidebarMenuItem>
              <button
                hlmSidebarMenuButton
                type="button"
                size="lg"
                side="top"
                align="start"
                [hlmDropdownMenuTrigger]="userMenu"
              >
                <span>{{ session.user()?.email }}</span>
              </button>
            </li>
          </ul>
        </div>
      </hlm-sidebar>

      <main hlmSidebarInset>
        <header class="flex h-12 shrink-0 items-center gap-2 border-b px-4">
          <button
            hlmSidebarTrigger
            type="button"
            aria-label="Toggle sidebar"
            srOnlyText="Toggle sidebar"
          ></button>
        </header>
        <div class="flex-1 p-4">
          <router-outlet />
        </div>
      </main>
    </div>

    <ng-template #userMenu>
      <hlm-dropdown-menu class="min-w-56">
        <button hlmDropdownMenuItem type="button" (triggered)="signOut()">Sign out</button>
      </hlm-dropdown-menu>
    </ng-template>
  `,
})
export class Shell {
  private readonly sectionsApi = inject(SectionsApi);
  private readonly router = inject(Router);
  private readonly queryParams = toSignal(inject(ActivatedRoute).queryParamMap, {
    requireSync: true,
  });

  private readonly uploadQueue = inject(UploadQueue);

  protected readonly session = inject(Session);
  protected readonly sections = signal<SectionResponse[]>([]);
  protected readonly signOutFailed = signal(false);
  /** The `section` the URL carries, which "Add books" passes on. */
  protected readonly browsedSection = computed(() => areaParam(readArea(this.queryParams())));
  protected readonly isAdmin = this.session.isAdmin;
  /** Whether the list of works is open, the only page "Library", "Default" and the sections stand for. */
  protected readonly onLibrary = isActive('/library', this.router, { queryParams: 'ignored' });
  protected readonly onUpload = isActive('/upload', this.router, { queryParams: 'ignored' });
  /**
   * The sidebar's links to the list of works: "Library", then "Default" and each section when there is at least one,
   * since the Default area is worth naming only beside a section. The one the URL names is current.
   */
  protected readonly links = computed(() => {
    const sections = this.sections();
    const areas: { label: string; area: LibraryArea }[] = [
      { label: 'Library', area: { kind: 'all' } },
      ...(sections.length > 0 ? [{ label: 'Default', area: { kind: 'default' } as const }] : []),
      ...sections.map(({ id, name }) => ({ label: name, area: { kind: 'section', id } as const })),
    ];
    const browsed = readArea(this.queryParams());
    return areas.map(({ label, area }) => ({
      label,
      section: areaParam(area),
      current: this.onLibrary() && sameArea(area, browsed),
    }));
  });

  constructor() {
    void this.loadSections();
  }

  protected async signOut(): Promise<void> {
    this.signOutFailed.set(false);
    try {
      await this.session.signOut();
    } catch {
      // The session is still good, so the user stays where they are instead of being shown as signed out.
      this.signOutFailed.set(true);
      return;
    }
    // The next person signed in on this tab must not see, or keep sending, this user's files.
    this.uploadQueue.reset();
    await this.router.navigateByUrl('/sign-in');
  }

  private async loadSections(): Promise<void> {
    try {
      this.sections.set(await this.sectionsApi.list());
    } catch {
      // Without the sections the sidebar still offers "Library", which is enough to keep going.
    }
  }
}
