import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { ApiService } from '../../core/api.service';
import { BackupInfo } from '../../core/models';
import { LoadingSpinner } from '../../shared/loading-spinner/loading-spinner';

/** Formats a byte count ("1.2 MB"). Pure function, so it is unit-testable. */
export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024)
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

/**
 * File name from a Content-Disposition header
 * (`attachment; filename="docuarchive-backup-20261127-073000.zip"`), or null.
 * Pure function, so it is unit-testable.
 */
export function fileNameFromContentDisposition(
  header: string | null,
): string | null {
  if (!header) return null;
  const match = /filename\*?=(?:UTF-8''|")?([^";]+)"?/i.exec(header);
  if (!match) return null;
  try {
    return decodeURIComponent(match[1]);
  } catch {
    return match[1];
  }
}

/**
 * Backup (Phase 8): a system administrator downloads the whole system — a
 * consistent database snapshot plus every stored file — as one timestamped
 * ZIP, and keeps it somewhere safe.
 *
 * Why this page exists: on the Render Free tier the database and the
 * uploaded files live on ephemeral disk and are wiped on every redeploy,
 * so the only backup that survives is one downloaded off the server.
 * Download before every deploy, at least, and keep copies off-site —
 * docs/BACKUP.md has the full restore procedure.
 *
 * Non-admins never see the download: the API answers 403 and the page says
 * so. On a PostgreSQL deployment the download is refused with 501 — in-app
 * backup is SQLite-only, and Postgres backups come from pg_dump or the
 * database provider (also docs/BACKUP.md).
 */
@Component({
  selector: 'app-backup',
  imports: [DatePipe, LoadingSpinner],
  templateUrl: './backup.html',
  styleUrl: './backup.css',
})
export class Backup implements OnInit {
  private readonly api = inject(ApiService);

  protected readonly info = signal<BackupInfo | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  /** True when the server said 403: this page is for system admins only. */
  protected readonly forbidden = signal(false);
  protected readonly downloading = signal(false);
  protected readonly downloadError = signal<string | null>(null);
  protected readonly downloaded = signal(false);
  protected readonly formatBytes = formatBytes;

  ngOnInit(): void {
    this.load();
  }

  /** Reloads what a backup would contain right now. */
  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.forbidden.set(false);
    this.api.getBackupInfo().subscribe({
      next: (info) => {
        this.loading.set(false);
        this.info.set(info);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        if (err instanceof HttpErrorResponse && err.status === 403) {
          this.forbidden.set(true);
          return;
        }
        this.error.set('Could not load the backup details. Please try again.');
      },
    });
  }

  /** Downloads the backup ZIP through the browser's save dialog. */
  protected download(): void {
    if (this.downloading()) return;
    this.downloading.set(true);
    this.downloadError.set(null);
    this.downloaded.set(false);
    this.api.downloadBackup().subscribe({
      next: (response) => {
        this.downloading.set(false);
        const blob = response.body;
        if (!blob) {
          this.downloadError.set(
            'The backup came back empty. Please try again.',
          );
          return;
        }
        const fileName =
          fileNameFromContentDisposition(
            response.headers.get('Content-Disposition'),
          ) ?? 'docuarchive-backup.zip';
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        a.click();
        URL.revokeObjectURL(url);
        this.downloaded.set(true);
      },
      error: (err: unknown) => {
        this.downloading.set(false);
        void this.problemDetail(err).then((detail) => {
          this.downloadError.set(
            detail ??
              'Could not download the backup. Please try again.',
          );
        });
      },
    });
  }

  /**
   * The server's problem-details message (`detail`), if the error carries
   * one. Download errors arrive as a Blob (the request asked for one), so
   * the JSON has to be read back out of it first.
   */
  private async problemDetail(err: unknown): Promise<string | null> {
    if (!(err instanceof HttpErrorResponse)) return null;
    if (err.error instanceof Blob) {
      try {
        const parsed = JSON.parse(await err.error.text()) as {
          detail?: unknown;
        };
        return typeof parsed.detail === 'string' ? parsed.detail : null;
      } catch {
        return null;
      }
    }
    if (err.error && typeof err.error === 'object' && 'detail' in err.error)
      return String(err.error.detail);
    return null;
  }
}
