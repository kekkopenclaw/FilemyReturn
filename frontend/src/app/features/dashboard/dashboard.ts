import { Component, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { DashboardStats } from '../../core/models';
import { WorkspaceService } from '../../core/workspace.service';
import { EmptyState } from '../../shared/empty-state/empty-state';
import { LoadingSpinner } from '../../shared/loading-spinner/loading-spinner';

/**
 * Workspace overview: real counts from GET /dashboard, nothing mocked.
 * Cards match the backend DashboardDto exactly (documentCount,
 * documentsReady, documentsProcessing, documentsFailed, ocrPending,
 * memberCount, archiveCount). There is no recent-uploads or storage field
 * on that endpoint, so none is shown here — see /documents instead.
 */
@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, EmptyState, LoadingSpinner],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.css',
})
export class Dashboard {
  private readonly api = inject(ApiService);
  protected readonly workspaceService = inject(WorkspaceService);

  protected readonly stats = signal<DashboardStats | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    // Reload whenever the selected workspace changes.
    effect(() => {
      const workspaceId = this.workspaceService.selectedWorkspaceId();
      if (workspaceId) {
        this.load(workspaceId);
      } else {
        this.stats.set(null);
        this.error.set(null);
      }
    });
  }

  private load(workspaceId: string): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.getDashboard(workspaceId).subscribe({
      next: (stats) => {
        this.stats.set(stats);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set(
          'Could not load the dashboard. Please check your connection and try again.',
        );
      },
    });
  }
}
