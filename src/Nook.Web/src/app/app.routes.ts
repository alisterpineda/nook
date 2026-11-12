import { Routes } from '@angular/router';
import { setupGuard, signedInGuard, signedOutGuard } from './core/guards';

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
    path: 'works',
    canActivate: [signedInGuard],
    loadComponent: () => import('./shell/shell').then((m) => m.Shell),
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./features/works/works-list-page').then((m) => m.WorksListPage),
      },
      {
        path: ':id',
        loadComponent: () =>
          import('./features/works/work-detail-page').then((m) => m.WorkDetailPage),
      },
    ],
  },
  { path: '**', redirectTo: 'works' },
];
