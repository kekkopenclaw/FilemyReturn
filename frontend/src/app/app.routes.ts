import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { Login } from './features/auth/login/login';
import { Register } from './features/auth/register/register';
import { Dashboard } from './features/dashboard/dashboard';
import { DocumentList } from './features/documents/document-list/document-list';
import { WorkspaceList } from './features/workspaces/workspace-list/workspace-list';
import { AppShell } from './shared/app-shell/app-shell';

export const routes: Routes = [
  { path: 'login', component: Login },
  { path: 'register', component: Register },
  {
    path: '',
    component: AppShell,
    canActivate: [authGuard],
    children: [
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
      { path: 'dashboard', component: Dashboard },
      { path: 'workspaces', component: WorkspaceList },
      { path: 'documents', component: DocumentList },
    ],
  },
  { path: '**', redirectTo: '/dashboard' },
];
