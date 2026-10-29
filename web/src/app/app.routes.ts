import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: '/files' },
  {
    path: 'auth/callback',
    loadComponent: () => import('./core/auth/auth-callback').then((m) => m.AuthCallback),
  },
  {
    path: 'files',
    canActivate: [authGuard],
    loadComponent: () => import('./features/files/files-list').then((m) => m.FilesList),
  },
  {
    path: 'files/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./features/files/files-detail').then((m) => m.FilesDetail),
  },
  {
    path: 'd/:token',
    loadComponent: () => import('./features/public/public-link').then((m) => m.PublicLink),
  },
  { path: '**', redirectTo: '/files' },
];
