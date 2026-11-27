import { DatePipe } from '@angular/common';
import { Component, effect, inject, signal } from '@angular/core';
import { ApiService } from '../../../core/api.service';
import { AuditLogEntry } from '../../../core/models';
import { WorkspaceService } from '../../../core/workspace.service';
import { EmptyState } from '../../../shared/empty-state/empty-state';
import { LoadingSpinner } from '../../../shared/loading-spinner/loading-spinner';

/** Audit trail for the selected workspace (newest first, read-only). */
@Component({
  selector: 'app-audit-log',
  imports: [DatePipe, EmptyState, LoadingSpinner],
  templateUrl: './audit-log.html',
  styleUrl: './audit-log.css',
})
export class AuditLog {
  private readonly api = inject(ApiService);
  protected readonly workspaceService = inject(WorkspaceService);

  protected readonly entries = signal<AuditLogEntry[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      const workspaceId = this.workspaceService.selectedWorkspaceId();
      if (workspaceId) {
        this.load(workspaceId);
      } else {
        this.entries.set([]);
        this.error.set(null);
      }
    });
  }

  protected shortId(id: string | null): string {
    return id ? `${id.slice(0, 8)}…` : '—';
  }

  private load(workspaceId: string): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.getAuditLogs(workspaceId).subscribe({
      next: (list) => {
        this.entries.set(list ?? []);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set(
          'Could not load the audit log. You may not have permission to view it.',
        );
      },
    });
  }
}
