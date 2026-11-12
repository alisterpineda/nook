import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink, RouterOutlet } from '@angular/router';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { LibrariesApi } from '../api/libraries-api';
import { components } from '../api/schema';
import { Session } from '../core/session';

type LibraryResponse = components['schemas']['LibraryResponse'];

/**
 * The frame around every signed-in page: a collapsible sidebar with "All works" and the user's libraries, the
 * user's menu in its footer, and the page itself beside it.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterLink, RouterOutlet, HlmDropdownMenuImports, HlmSidebarImports],
  template: `
    <div hlmSidebarWrapper>
      <hlm-sidebar>
        <div hlmSidebarContent>
          <nav hlmSidebarGroup aria-label="Libraries">
            <ul hlmSidebarMenu>
              <li hlmSidebarMenuItem>
                <!-- Links name only the library, so choosing one drops the page and sort of the list before. -->
                <a
                  hlmSidebarMenuButton
                  routerLink="/works"
                  closeMobileSidebarOnClick
                  [isActive]="selectedLibraryId() === null"
                  [attr.aria-current]="selectedLibraryId() === null ? 'page' : null"
                >
                  <span>All works</span>
                </a>
              </li>
              @for (library of libraries(); track library.id) {
                <li hlmSidebarMenuItem>
                  <a
                    hlmSidebarMenuButton
                    routerLink="/works"
                    [queryParams]="{ library: library.id }"
                    closeMobileSidebarOnClick
                    [isActive]="selectedLibraryId() === library.id"
                    [attr.aria-current]="selectedLibraryId() === library.id ? 'page' : null"
                  >
                    <span>{{ library.name }}</span>
                  </a>
                </li>
              }
            </ul>
          </nav>
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
  private readonly librariesApi = inject(LibrariesApi);
  private readonly router = inject(Router);
  private readonly queryParams = toSignal(inject(ActivatedRoute).queryParamMap, {
    requireSync: true,
  });

  protected readonly session = inject(Session);
  protected readonly libraries = signal<LibraryResponse[]>([]);
  protected readonly signOutFailed = signal(false);
  /** The library the URL names, or `null` for "All works". */
  protected readonly selectedLibraryId = computed(() => this.queryParams().get('library'));

  constructor() {
    void this.loadLibraries();
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
    await this.router.navigateByUrl('/sign-in');
  }

  private async loadLibraries(): Promise<void> {
    try {
      this.libraries.set(await this.librariesApi.list());
    } catch {
      // Without the libraries the sidebar still offers "All works", which is enough to keep going.
    }
  }
}
