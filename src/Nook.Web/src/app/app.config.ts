import { provideHttpClient, withXhr } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { AppTheme } from './core/app-theme';
import { AppUpdate } from './core/app-update';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withXhr()),
    provideAppInitializer(() => inject(AppTheme).start()),
    provideAppInitializer(() => inject(AppUpdate).start()),
  ],
};
