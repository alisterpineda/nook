import { Routes } from '@angular/router';
import { adminGuard, setupGuard, signedInGuard, signedOutGuard } from './core/guards';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'works' },
  {
    path: 'setup',
    canActivate: [setupGuard],
    loadComponent: () => import('./features/setup/setup-page').then((m) => m.SetupPage),
  },
  {
    path: 'sign-in',
    canActivate: [signedOutGuard],
    loadComponent: () => import('./features/sign-in/sign-in-page').then((m) => m.SignInPage),
  },
  {
    // One shell holds every signed-in page, so it and the services under it stay put while the user moves between
    // them.
    path: '',
    canActivate: [signedInGuard],
    loadComponent: () => import('./shell/shell').then((m) => m.Shell),
    children: [
      {
        path: 'works',
        loadComponent: () =>
          import('./features/works/works-list-page').then((m) => m.WorksListPage),
      },
      {
        path: 'works/:id',
        loadComponent: () =>
          import('./features/works/work-detail-page').then((m) => m.WorkDetailPage),
      },
      {
        path: 'upload',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/upload/upload-page').then((m) => m.UploadPage),
      },
    ],
  },
  { path: '**', redirectTo: 'works' },
];
