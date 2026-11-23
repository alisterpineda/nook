import { Routes } from '@angular/router';
import { tabTitle } from './core/app-name';
import { adminGuard, libraryGuard, setupGuard, signedOutGuard } from './core/guards';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'library' },
  {
    path: 'setup',
    title: tabTitle('Set up'),
    canActivate: [setupGuard],
    loadComponent: () => import('./features/setup/setup-page').then((m) => m.SetupPage),
  },
  {
    path: 'sign-in',
    title: tabTitle('Sign in'),
    canActivate: [signedOutGuard],
    loadComponent: () => import('./features/sign-in/sign-in-page').then((m) => m.SignInPage),
  },
  {
    // The reader fills the screen, so it stands outside the shell and its sidebar.
    path: 'works/:workId/read/:itemId',
    title: tabTitle(),
    canActivate: [libraryGuard],
    loadComponent: () => import('./features/reader/reader-page').then((m) => m.ReaderPage),
  },
  {
    // One shell holds every page of the library, so it and the services under it stay put while the user moves
    // between them.
    path: '',
    canActivate: [libraryGuard],
    loadComponent: () => import('./shell/shell').then((m) => m.Shell),
    children: [
      {
        path: 'library',
        loadComponent: () =>
          import('./features/works/works-list-page').then((m) => m.WorksListPage),
      },
      {
        path: 'works/:id',
        loadComponent: () =>
          import('./features/works/work-detail-page').then((m) => m.WorkDetailPage),
      },
      {
        path: 'authors',
        loadComponent: () =>
          import('./features/people/authors-list-page').then((m) => m.AuthorsListPage),
      },
      {
        path: 'people/:id',
        loadComponent: () => import('./features/people/person-page').then((m) => m.PersonPage),
      },
      {
        // One guard keeps every page under it to admins.
        path: 'admin',
        canActivate: [adminGuard],
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'users' },
          {
            path: 'users',
            loadComponent: () => import('./features/users/users-page').then((m) => m.UsersPage),
          },
          {
            path: 'public-access',
            loadComponent: () =>
              import('./features/public-access/public-access-page').then((m) => m.PublicAccessPage),
          },
          {
            path: 'jobs',
            loadComponent: () => import('./features/jobs/jobs-page').then((m) => m.JobsPage),
          },
          {
            path: 'audit-log',
            loadComponent: () =>
              import('./features/audit-log/audit-log-page').then((m) => m.AuditLogPage),
          },
        ],
      },
    ],
  },
  { path: '**', redirectTo: 'library' },
];
