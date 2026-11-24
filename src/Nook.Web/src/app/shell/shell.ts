import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import {
  ActivatedRoute,
  NavigationEnd,
  Router,
  RouterLink,
  RouterOutlet,
  isActive,
} from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBookOpen,
  lucideChevronsUpDown,
  lucideFolder,
  lucideGlobe,
  lucideLibraryBig,
  lucideListChecks,
  lucideLogOut,
  lucideScrollText,
  lucideSearch,
  lucideSunMoon,
  lucideUpload,
  lucideUserCog,
  lucideUsers,
} from '@ng-icons/lucide';
import { HlmBreadcrumbImports } from '@spartan-ng/helm/breadcrumb';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogService } from '@spartan-ng/helm/dialog';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { filter, map } from 'rxjs';
import { LibraryArea, areaParam, readArea, sameArea } from '../core/library-area';
import { allows } from '../core/content-access';
import { appName, tabTitle } from '../core/app-name';
import { PageName } from '../core/page-name';
import { readTerm } from '../core/search-term';
import { Sections, defaultAreaName, libraryName } from '../core/sections';
import { ActivityHub } from '../core/activity-hub';
import { Session } from '../core/session';
import { ActivityButton } from '../features/activity/activity-button';
import { UploadActivity } from '../features/activity/upload-activity';
import { AddBooks } from '../features/upload/add-books';
import { DropOverlay } from '../features/upload/drop-overlay';
import { UploadQueue } from '../features/upload/upload-queue';
import { CommandPalette } from './command-palette';
import { ThemeDialog } from './theme-dialog';

/** One step of the header's trail to the page that is open. */
interface Crumb {
  /** What the step is called, or `null` while the page has not said. */
  label: string | null;
  /** Where the step leads. The last step, the page that is open, leads nowhere, and neither does "Admin". */
  link?: string;
}

/**
 * The frame around every page of the library: a sidebar that collapses to its icons, with the app's name, then
 * "Library", then "Default" and the caller's sections when there are any, then "Authors", then "Users", "Public access",
 * "Jobs" and "Audit log" under "Admin" for an admin, and in its footer "Upload", which opens the "Add books" dialog,
 * for whoever may upload, above the user's menu, which opens a dialog for the app's theme, and the page itself beside
 * it under a header that says where the page is. A visitor nobody has signed in as gets no footer, and a "Sign in"
 * link in the header.
 */
@Component({
  selector: 'app-shell',
  imports: [
    NgIcon,
    RouterLink,
    RouterOutlet,
    HlmBreadcrumbImports,
    HlmButtonImports,
    HlmDropdownMenuImports,
    HlmSidebarImports,
    HlmSkeletonImports,
    ActivityButton,
    CommandPalette,
    DropOverlay,
  ],
  providers: [
    provideIcons({
      lucideBookOpen,
      lucideChevronsUpDown,
      lucideFolder,
      lucideGlobe,
      lucideLibraryBig,
      lucideListChecks,
      lucideLogOut,
      lucideScrollText,
      lucideSearch,
      lucideSunMoon,
      lucideUpload,
      lucideUserCog,
      lucideUsers,
    }),
  ],
  template: `
    <div hlmSidebarWrapper>
      <hlm-sidebar collapsible="icon">
        <div hlmSidebarHeader>
          <ul hlmSidebarMenu>
            <li hlmSidebarMenuItem>
              <a hlmSidebarMenuButton size="lg" routerLink="/library" closeMobileSidebarOnClick>
                <span
                  class="bg-sidebar-primary text-sidebar-primary-foreground flex aspect-square size-8 items-center justify-center rounded-lg"
                  aria-hidden="true"
                >
                  <ng-icon name="lucideBookOpen" />
                </span>
                <span class="text-base font-semibold tracking-tight">{{ appName }}</span>
              </a>
            </li>
          </ul>
        </div>
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
                    [tooltip]="link.label"
                    [isActive]="link.current"
                    [attr.aria-current]="link.current ? 'page' : null"
                  >
                    <ng-icon [name]="link.icon" />
                    <span>{{ link.label }}</span>
                  </a>
                </li>
              }
            </ul>
          </nav>
          <nav hlmSidebarGroup aria-label="Browse">
            <div hlmSidebarGroupLabel>Browse</div>
            <ul hlmSidebarMenu>
              <li hlmSidebarMenuItem>
                <a
                  hlmSidebarMenuButton
                  routerLink="/authors"
                  closeMobileSidebarOnClick
                  tooltip="Authors"
                  [isActive]="onAuthors()"
                  [attr.aria-current]="onAuthors() ? 'page' : null"
                >
                  <ng-icon name="lucideUsers" />
                  <span>Authors</span>
                </a>
              </li>
            </ul>
          </nav>
          @if (isAdmin()) {
            <nav hlmSidebarGroup aria-label="Admin">
              <div hlmSidebarGroupLabel>Admin</div>
              <ul hlmSidebarMenu>
                @for (link of adminLinks; track link.path) {
                  <li hlmSidebarMenuItem>
                    <a
                      hlmSidebarMenuButton
                      [routerLink]="link.path"
                      closeMobileSidebarOnClick
                      [tooltip]="link.label"
                      [isActive]="link.current()"
                      [attr.aria-current]="link.current() ? 'page' : null"
                    >
                      <ng-icon [name]="link.icon" />
                      <span>{{ link.label }}</span>
                    </a>
                  </li>
                }
              </ul>
            </nav>
          }
        </div>
        @if (session.user(); as user) {
          <div hlmSidebarFooter>
            @if (session.canUpload()) {
              <ul hlmSidebarMenu>
                <li hlmSidebarMenuItem>
                  <!-- Preselects the section being browsed, Default included. -->
                  <button
                    hlmSidebarMenuButton
                    type="button"
                    closeMobileSidebarOnClick
                    tooltip="Upload"
                    (click)="addBooks.open([], browsedSection())"
                  >
                    <ng-icon name="lucideUpload" />
                    <span>Upload</span>
                  </button>
                </li>
              </ul>
            }
            @if (signOutFailed()) {
              <p
                class="text-destructive px-2 text-sm group-data-[collapsible=icon]:hidden"
                role="alert"
              >
                Could not sign out. Try again.
              </p>
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
                  <span
                    class="bg-sidebar-accent text-sidebar-accent-foreground flex aspect-square size-8 items-center justify-center rounded-lg border text-sm font-medium uppercase"
                    aria-hidden="true"
                    >{{ user.email.charAt(0) }}</span
                  >
                  <span class="min-w-0 flex-1 truncate" data-user-email>{{ user.email }}</span>
                  <ng-icon name="lucideChevronsUpDown" class="text-muted-foreground ms-auto" />
                </button>
              </li>
            </ul>
          </div>
        }
        <button hlmSidebarRail type="button" aria-label="Toggle sidebar"></button>
      </hlm-sidebar>

      <main hlmSidebarInset>
        <!-- Sticky with an opaque background on purpose: an iPhone Home Screen app on iOS 26 or later blurs the
             top of the page under the status bar unless a sizeable fixed or sticky, opaque element touches the
             top edge. -->
        <header
          class="bg-background sticky top-0 z-20 flex h-14 shrink-0 items-center gap-2 border-b px-4"
        >
          <button
            hlmSidebarTrigger
            type="button"
            class="-ms-1"
            aria-label="Toggle sidebar"
            srOnlyText="Toggle sidebar"
          ></button>
          <div class="bg-border me-2 h-4 w-px max-md:hidden" aria-hidden="true"></div>
          <nav hlmBreadcrumb class="min-w-0 flex-1 max-md:hidden" aria-label="Breadcrumb">
            <ol hlmBreadcrumbList class="flex-nowrap">
              @for (crumb of crumbs(); track $index) {
                <li hlmBreadcrumbItem class="min-w-0">
                  @if (crumb.label === null) {
                    <span hlmSkeleton class="h-4 w-32"></span>
                  } @else if (crumb.link) {
                    <a hlmBreadcrumbLink [link]="crumb.link">{{ crumb.label }}</a>
                  } @else if ($last) {
                    <span hlmBreadcrumbPage class="truncate">{{ crumb.label }}</span>
                  } @else {
                    <span>{{ crumb.label }}</span>
                  }
                </li>
                @if (!$last) {
                  <li hlmBreadcrumbSeparator></li>
                }
              }
            </ol>
          </nav>
          <div class="flex-1 md:hidden"></div>
          <button
            type="button"
            aria-label="Search"
            class="border-input text-muted-foreground hover:bg-accent hidden h-9 w-64 items-center gap-2 rounded-md border px-3 text-sm md:flex"
            (click)="palette.open()"
          >
            <ng-icon name="lucideSearch" class="shrink-0" />
            <span class="flex-1 text-start">Search…</span>
            <kbd
              class="bg-muted text-muted-foreground rounded border px-1.5 font-mono text-xs"
              aria-hidden="true"
              >⌘K</kbd
            >
          </button>
          <button
            hlmBtn
            variant="ghost"
            size="icon"
            type="button"
            aria-label="Search"
            class="md:hidden"
            (click)="palette.open()"
          >
            <ng-icon name="lucideSearch" />
          </button>
          @if (session.user()) {
            <app-activity-button />
          }
          @if (!session.user()) {
            <a hlmBtn variant="outline" size="sm" routerLink="/sign-in">Sign in</a>
          }
        </header>
        <div class="flex-1 p-4 md:p-6">
          <router-outlet />
        </div>
      </main>
    </div>

    <app-drop-overlay [section]="browsedSection()" />
    <app-command-palette #palette />

    <ng-template #userMenu>
      <hlm-dropdown-menu class="min-w-56">
        <div hlmDropdownMenuLabel class="text-muted-foreground truncate text-xs font-normal">
          {{ session.user()?.email }}
        </div>
        <div hlmDropdownMenuSeparator></div>
        <button hlmDropdownMenuItem type="button" (triggered)="openTheme()">
          <ng-icon name="lucideSunMoon" />
          Theme
        </button>
        <div hlmDropdownMenuSeparator></div>
        <button hlmDropdownMenuItem type="button" (triggered)="signOut()">
          <ng-icon name="lucideLogOut" />
          Sign out
        </button>
      </hlm-dropdown-menu>
    </ng-template>
  `,
})
export class Shell {
  private readonly sectionStore = inject(Sections);
  private readonly pageName = inject(PageName);
  private readonly title = inject(Title);
  private readonly router = inject(Router);
  private readonly queryParams = toSignal(inject(ActivatedRoute).queryParamMap, {
    requireSync: true,
  });

  protected readonly addBooks = inject(AddBooks);
  private readonly uploadQueue = inject(UploadQueue);
  private readonly uploadActivity = inject(UploadActivity);
  private readonly hub = inject(ActivityHub);

  protected readonly session = inject(Session);
  private readonly dialogs = inject(HlmDialogService);
  protected readonly appName = appName;
  protected readonly sections = this.sectionStore.list;
  protected readonly signOutFailed = signal(false);
  /** The `section` the URL carries, which the "Add books" dialog preselects. */
  protected readonly browsedSection = computed(() => areaParam(readArea(this.queryParams())));
  protected readonly isAdmin = this.session.isAdmin;
  /** Whether the list of works is open, the only page "Library", "Default" and the sections stand for. */
  protected readonly onLibrary = isActive('/library', this.router, { queryParams: 'ignored' });
  /** The admin's pages, in the sidebar's order: who gets in, then what has happened. */
  protected readonly adminLinks = adminPages.map((page) => ({
    ...page,
    current: isActive(page.path, this.router, { queryParams: 'ignored' }),
  }));
  protected readonly onAuthors = isActive('/authors', this.router, { queryParams: 'ignored' });
  /**
   * The sidebar's links to the list of works: "Library", then "Default" and each section when there is at least one,
   * since the Default area is worth naming only beside a section. "Default" is left out for a visitor who may not
   * see it. The one the URL names is current.
   */
  protected readonly links = computed(() => {
    const sections = this.sections();
    const showDefault = sections.length > 0 && allows(this.session.defaultAreaAccess(), 'browse');
    const areas: { label: string; icon: string; area: LibraryArea }[] = [
      { label: libraryName, icon: 'lucideLibraryBig', area: { kind: 'all' } },
      ...(showDefault
        ? [{ label: defaultAreaName, icon: 'lucideFolder', area: { kind: 'default' } as const }]
        : []),
      ...sections.map(({ id, name }) => ({
        label: name,
        icon: 'lucideFolder',
        area: { kind: 'section', id } as const,
      })),
    ];
    const browsed = readArea(this.queryParams());
    return areas.map(({ label, icon, area }) => ({
      label,
      icon,
      section: areaParam(area),
      current: this.onLibrary() && sameArea(area, browsed),
    }));
  });

  /** The path of the page that is open, without its query. */
  private readonly path = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => pathOf(this.router.url)),
    ),
    { initialValue: pathOf(this.router.url) },
  );

  /**
   * The header's trail: the list the page belongs to, then what the page narrows it to or the work or person it
   * shows. A search covers every section, so it stands under "Library" alone.
   */
  protected readonly crumbs = computed<Crumb[]>(() => {
    const path = this.path();
    // The pages name themselves under the id the router hands them, which is the URL's without its encoding or any
    // parameters after a semicolon.
    const detail = /^\/(works|people)\/([^/;]+)(?:;[^/]*)?$/.exec(path);
    if (detail) {
      const [, kind, encodedId] = detail;
      const id = decoded(encodedId);
      const list: Crumb =
        kind === 'works'
          ? { label: libraryName, link: '/library' }
          : { label: 'Authors', link: '/authors' };
      return [list, { label: this.pageName.of(id) }];
    }
    if (path === '/authors') {
      return [{ label: 'Authors' }];
    }
    const adminPage = adminPages.find((page) => page.path === path);
    if (adminPage) {
      return [{ label: 'Admin' }, { label: adminPage.label }];
    }
    const area = readArea(this.queryParams());
    const narrowedTo =
      readTerm(this.queryParams()) !== null
        ? 'Search'
        : area.kind === 'all'
          ? null
          : (this.sectionStore.nameOf(area) ?? 'Section');
    return narrowedTo === null
      ? [{ label: libraryName }]
      : [{ label: libraryName, link: '/library' }, { label: narrowedTo }];
  });

  constructor() {
    void this.sectionStore.load();
    // The hub's connection is open for as long as someone is signed in, for whatever follows it: someone who may
    // upload follows their uploads from the server, and an admin's Jobs page hears of every job.
    effect(() => {
      const signedIn = this.session.user() !== null;
      const canUpload = this.session.canUpload();
      untracked(() => {
        if (signedIn) {
          // A connection that cannot open is tried again by the hub.
          this.hub.start().catch(() => undefined);
        } else {
          void this.closeHub();
        }
        if (canUpload) {
          this.uploadActivity.start();
        } else {
          this.uploadActivity.stop();
        }
      });
    });
    // The tab is named after the page that is open, once the page is known by name.
    effect(() => {
      const page = this.crumbs().at(-1)?.label;
      this.title.setTitle(tabTitle(page));
    });
  }

  /** Opens the dialog that chooses the app's theme. */
  protected openTheme(): void {
    this.dialogs.open(ThemeDialog, { contentClass: 'sm:max-w-sm' });
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
    // The next person signed in on this tab must not see, or keep sending, this user's files, nor see the names of
    // what this user opened.
    this.uploadQueue.reset();
    this.uploadActivity.stop();
    await this.closeHub();
    this.pageName.clear();
    await this.router.navigateByUrl('/sign-in');
  }

  private async closeHub(): Promise<void> {
    try {
      await this.hub.stop();
    } catch {
      // A connection that will not close cleanly is gone all the same.
    }
  }
}

/** The pages only an admin may open. */
const adminPages = [
  { label: 'Users', path: '/admin/users', icon: 'lucideUserCog' },
  { label: 'Public access', path: '/admin/public-access', icon: 'lucideGlobe' },
  { label: 'Jobs', path: '/admin/jobs', icon: 'lucideListChecks' },
  { label: 'Audit log', path: '/admin/audit-log', icon: 'lucideScrollText' },
] as const;

/** The path of `url`, without its query or fragment. */
function pathOf(url: string): string {
  return url.split(/[?#]/)[0];
}

/** `segment` of a URL's path as it was before it was encoded, or as it is when it is not validly encoded. */
function decoded(segment: string): string {
  try {
    return decodeURIComponent(segment);
  } catch {
    return segment;
  }
}
