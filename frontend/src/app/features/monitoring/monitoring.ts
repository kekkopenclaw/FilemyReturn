import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { ApiService } from '../../core/api.service';
import { MonitoringStatus } from '../../core/models';
import { LoadingSpinner } from '../../shared/loading-spinner/loading-spinner';
import { formatBytes } from '../backup/backup';

/**
 * Formats process uptime in seconds ("2h 14m", "45s", "3d 2h").
 * Pure function, so it is unit-testable.
 */
export function formatUptime(totalSeconds: number): string {
  const seconds = Math.max(0, Math.floor(totalSeconds));
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const secs = seconds % 60;
  if (days > 0) return `${days}d ${hours}h`;
  if (hours > 0) return `${hours}h ${minutes}m`;
  if (minutes > 0) return `${minutes}m ${secs}s`;
  return `${secs}s`;
}

/**
 * Monitoring (Phase 9): a system administrator sees a read-only health
 * snapshot of the whole system — process uptime, app version and deployed
 * commit, database provider and size, counts (users, workspaces, documents
 * by status, OCR queue, human-review queue), stored files, failed logins in
 * the last 24h, and when the last backup was downloaded.
 *
 * In-app only, by design: there are no external uptime pings and no alerts
 * (that would need a third-party account — see docs/MONITORING.md). The
 * numbers are a point in time, so the page has a manual Refresh button
 * instead of polling.
 *
 * Non-admins never see the numbers: the API answers 403 and the page says
 * so. The response carries no secrets and no per-user or per-workspace
 * detail — only system-wide aggregates.
 */
@Component({
  selector: 'app-monitoring',
  imports: [DatePipe, LoadingSpinner],
  templateUrl: './monitoring.html',
  styleUrl: './monitoring.css',
})
export class Monitoring implements OnInit {
  private readonly api = inject(ApiService);

  protected readonly status = signal<MonitoringStatus | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  /** True when the server said 403: this page is for system admins only. */
  protected readonly forbidden = signal(false);
  protected readonly formatBytes = formatBytes;
  protected readonly formatUptime = formatUptime;

  ngOnInit(): void {
    this.load();
  }

  /** Reloads the health snapshot. */
  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.forbidden.set(false);
    this.api.getMonitoring().subscribe({
      next: (status) => {
        this.loading.set(false);
        this.status.set(status);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        if (err instanceof HttpErrorResponse && err.status === 403) {
          this.forbidden.set(true);
          return;
        }
        this.error.set('Could not load the monitoring snapshot. Please try again.');
      },
    });
  }
}
