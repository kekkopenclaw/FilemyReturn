import { Component, inject, OnInit } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { WorkspaceService } from '../../core/workspace.service';
import { LoadingSpinner } from '../loading-spinner/loading-spinner';
import { WorkspaceSelector } from '../workspace-selector/workspace-selector';

/** Authenticated layout: top bar, sidebar navigation, page outlet. */
@Component({
  selector: 'app-shell',
  imports: [
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    LoadingSpinner,
    WorkspaceSelector,
  ],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.css',
})
export class AppShell implements OnInit {
  protected readonly auth = inject(AuthService);
  protected readonly workspaceService = inject(WorkspaceService);

  ngOnInit(): void {
    // Load the user's workspaces once per session; failures surface via
    // the interceptor (401) or the page-level error states.
    this.workspaceService.loadWorkspaces().subscribe({
      error: () => {
        // Keep the shell usable even if workspaces fail to load; each page
        // shows its own error state.
      },
    });
  }

  protected logout(): void {
    this.auth.logout();
  }
}
