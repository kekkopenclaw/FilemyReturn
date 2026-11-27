import { DatePipe } from '@angular/common';
import { Component, effect, inject, signal } from '@angular/core';
import { ApiService } from '../../../core/api.service';
import { DocumentSummary } from '../../../core/models';
import { WorkspaceService } from '../../../core/workspace.service';
import { EmptyState } from '../../../shared/empty-state/empty-state';
import { LoadingSpinner } from '../../../shared/loading-spinner/loading-spinner';

/**
 * Document list for the selected workspace. Phase 1 has no uploads yet, so
 * the API returns an empty array and the empty state is the normal view.
 * No upload button is shown — uploads arrive in Phase 2.
 */
@Component({
  selector: 'app-document-list',
  imports: [DatePipe, EmptyState, LoadingSpinner],
  templateUrl: './document-list.html',
  styleUrl: './document-list.css',
})
export class DocumentList {
  private readonly api = inject(ApiService);
  protected readonly workspaceService = inject(WorkspaceService);

  protected readonly documents = signal<DocumentSummary[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      const workspaceId = this.workspaceService.selectedWorkspaceId();
      if (workspaceId) {
        this.load(workspaceId);
      } else {
        this.documents.set([]);
        this.error.set(null);
      }
    });
  }

  protected formatBytes(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    if (bytes < 1024 * 1024 * 1024)
      return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
  }

  protected statusClass(status: string): string {
    switch (status) {
      case 'Processed':
        return 'badge-success';
      case 'Failed':
        return 'badge-danger';
      case 'AwaitingReview':
        return 'badge-warning';
      case 'Processing':
        return 'badge-info';
      default:
        return 'badge-neutral';
    }
  }

  protected statusLabel(status: string): string {
    switch (status) {
      case 'AwaitingReview':
        return 'Awaiting review';
      default:
        return status;
    }
  }

  private load(workspaceId: string): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.getDocuments(workspaceId).subscribe({
      next: (docs) => {
        this.documents.set(docs);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set(
          'Could not load documents. Please check your connection and try again.',
        );
      },
    });
  }
}
