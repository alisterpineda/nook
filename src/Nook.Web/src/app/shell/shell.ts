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
  lucideUpload,
  lucideUsers,
} from '@ng-icons/lucide';
import { HlmBreadcrumbImports } from '@spartan-ng/helm/breadcrumb';
import { HlmButtonImports } from '@spartan-ng/helm/button';
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
import { SearchBox } from './search-box';

/** One step of the header's trail to the page that is open. */
interface Crumb {
  /** What the step is called, or `null` while the page has not said. */
  label: string | null;
  /** Where the step leads. The last step, the page that is open, leads nowhere. */
  link?: string;
}

/**
 * The frame around every page of the library: a sidebar that collapses to its icons, with the app's name, then
 * "Library", then "Default" and the caller's sections when there are any, then "Authors" and, for whoever may upload,
 * "Add books", which opens a dialog, then "Public access", "Jobs" and "Audit log" for an admin, the user's menu in its footer, and the page itself beside
 * it under a header that says where the page is. A visitor nobody has signed in as gets no user menu, and a "Sign in" link in the header.
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
    SearchBox,
    ActivityButton,
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
      lucideUpload,
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
              @if (session.canUpload()) {
                <li hlmSidebarMenuItem>
                  <!-- Preselects the section being browsed, Default included. -->
                  <button
                    hlmSidebarMenuButton
                    type="button"
                    closeMobileSidebarOnClick
                    tooltip="Add books"
                    (click)="addBooks.open([], browsedSection())"
                  >
                    <ng-icon name="lucideUpload" />
                    <span>Add books</span>
                  </button>
                </li>
              }
            </ul>
          </nav>
          @if (isAdmin()) {
            <nav hlmSidebarGroup aria-label="Manage">
              <div hlmSidebarGroupLabel>Manage</div>
              <ul hlmSidebarMenu>
                <li hlmSidebarMenuItem>
                  <a
                    hlmSidebarMenuButton
                    routerLink="/public-access"
                    closeMobileSidebarOnClick
                    tooltip="Public access"
                    [isActive]="onPublicAccess()"
                    [attr.aria-current]="onPublicAccess() ? 'page' : null"
                  >
                    <ng-icon name="lucideGlobe" />
                    <span>Public access</span>
                  </a>
                </li>
                <li hlmSidebarMenuItem>
                  <a
                    hlmSidebarMenuButton
                    routerLink="/jobs"
                    closeMobileSidebarOnClick
                    tooltip="Jobs"
                    [isActive]="onJobs()"
                    [attr.aria-current]="onJobs() ? 'page' : null"
                  >
                    <ng-icon name="lucideListChecks" />
                    <span>Jobs</span>
                  </a>
                </li>
                <li hlmSidebarMenuItem>
                  <a
                    hlmSidebarMenuButton
                    routerLink="/audit-log"
                    closeMobileSidebarOnClick
                    tooltip="Audit log"
                    [isActive]="onAuditLog()"
                    [attr.aria-current]="onAuditLog() ? 'page' : null"
                  >
                    <ng-icon name="lucideScrollText" />
                    <span>Audit log</span>
                  </a>
                </li>
              </ul>
            </nav>
          }
        </div>
        @if (session.user(); as user) {
          <div hlmSidebarFooter>
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
        <header class="flex h-14 shrink-0 items-center gap-2 border-b px-4">
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
                  } @else {
                    <span hlmBreadcrumbPage class="truncate">{{ crumb.label }}</span>
                  }
                </li>
                @if (!$last) {
                  <li hlmBreadcrumbSeparator></li>
                }
              }
            </ol>
          </nav>
          <app-search-box class="flex-1 md:w-72 md:flex-none" />
          @if (session.canUpload()) {
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

    <ng-template #userMenu>
      <hlm-dropdown-menu class="min-w-56">
        <div hlmDropdownMenuLabel class="text-muted-foreground truncate text-xs font-normal">
          {{ session.user()?.email }}
        </div>
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
  protected readonly appName = appName;
  protected readonly sections = this.sectionStore.list;
  protected readonly signOutFailed = signal(false);
  /** The `section` the URL carries, which "Add books" preselects. */
  protected readonly browsedSection = computed(() => areaParam(readArea(this.queryParams())));
  protected readonly isAdmin = this.session.isAdmin;
  /** Whether the list of works is open, the only page "Library", "Default" and the sections stand for. */
  protected readonly onLibrary = isActive('/library', this.router, { queryParams: 'ignored' });
  protected readonly onPublicAccess = isActive('/public-access', this.router, {
    queryParams: 'ignored',
  });
  protected readonly onJobs = isActive('/jobs', this.router, { queryParams: 'ignored' });
  protected readonly onAuditLog = isActive('/audit-log', this.router, { queryParams: 'ignored' });
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
    switch (path) {
      case '/authors':
        return [{ label: 'Authors' }];
      case '/public-access':
        return [{ label: 'Public access' }];
      case '/jobs':
        return [{ label: 'Jobs' }];
      case '/audit-log':
        return [{ label: 'Audit log' }];
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
    // Someone who may upload follows their uploads from the server for as long as they are signed in, and an admin
    // hears of every job. Both listen on the one connection, which is open for as long as either applies.
    effect(() => {
      const canUpload = this.session.canUpload();
      const isAdmin = this.isAdmin();
      untracked(() => {
        if (canUpload) {
          this.uploadActivity.start();
        } else {
          void this.uploadActivity.stop();
        }
        if (isAdmin) {
          // Does nothing when the uploads have opened it. A connection that cannot open is tried again by the hub.
          this.hub.start().catch(() => undefined);
        }
      });
    });
    // The tab is named after the page that is open, once the page is known by name.
    effect(() => {
      const page = this.crumbs().at(-1)?.label;
      this.title.setTitle(tabTitle(page));
    });
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
    await this.uploadActivity.stop();
    this.pageName.clear();
    await this.router.navigateByUrl('/sign-in');
  }
}

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
