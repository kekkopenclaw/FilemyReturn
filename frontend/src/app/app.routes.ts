import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { landingGuard } from './core/landing.guard';
import { ChangePassword } from './features/auth/change-password/change-password';
import { Login } from './features/auth/login/login';
import { Register } from './features/auth/register/register';
import { Landing } from './features/landing/landing';
import { ArchiveList } from './features/archives/archive-list/archive-list';
import { AuditLog } from './features/audit/audit-log/audit-log';
import { Dashboard } from './features/dashboard/dashboard';
import { DocumentList } from './features/documents/document-list/document-list';
import { FilingRules } from './features/filing-rules/filing-rules';
import { Search } from './features/search/search/search';
import { WorkspaceList } from './features/workspaces/workspace-list/workspace-list';
import { AppShell } from './shared/app-shell/app-shell';

export const routes: Routes = [
  // Public home page: signed-in users are bounced to /dashboard by the guard.
  { path: '', component: Landing, canActivate: [landingGuard] },
  { path: 'login', component: Login },
  { path: 'register', component: Register },
  // Public on purpose: the first-login flow (MustChangePassword) has no
  // tokens yet, so the user must be able to reach this page signed out.
  { path: 'change-password', component: ChangePassword },
  {
    path: '',
    component: AppShell,
    canActivate: [authGuard],
    children: [
      { path: 'dashboard', component: Dashboard },
      { path: 'workspaces', component: WorkspaceList },
      { path: 'documents', component: DocumentList },
      { path: 'search', component: Search },
      { path: 'archives', component: ArchiveList },
      { path: 'filing-rules', component: FilingRules },
      { path: 'audit-logs', component: AuditLog },
    ],
  },
  { path: '**', redirectTo: '' },
];
