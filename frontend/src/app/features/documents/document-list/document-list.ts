import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, effect, inject, signal } from '@angular/core';
import { ApiService } from '../../../core/api.service';
import { DocumentSummary } from '../../../core/models';
import { WorkspaceService } from '../../../core/workspace.service';
import { EmptyState } from '../../../shared/empty-state/empty-state';
import { LoadingSpinner } from '../../../shared/loading-spinner/loading-spinner';

/**
 * Document list for the selected workspace, with upload and download.
 * Statuses match the backend DocumentStatus enum exactly:
 * Uploaded / Processing / Ready / Failed.
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
  protected readonly uploading = signal(false);
  protected readonly uploadError = signal<string | null>(null);
  protected readonly uploadSuccess = signal(false);
  protected readonly downloadingId = signal<string | null>(null);

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
      case 'Ready':
        return 'badge-success';
      case 'Failed':
        return 'badge-danger';
      case 'Processing':
        return 'badge-info';
      case 'Uploaded':
      default:
        return 'badge-neutral';
    }
  }

  protected statusLabel(status: string): string {
    return status;
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    // Reset so the same file can be chosen again after an upload.
    input.value = '';
    if (!file) return;
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    if (!workspaceId) return;
    this.uploading.set(true);
    this.uploadError.set(null);
    this.uploadSuccess.set(false);
    this.api.uploadDocument(workspaceId, file).subscribe({
      next: () => {
        this.uploading.set(false);
        this.uploadSuccess.set(true);
        this.load(workspaceId);
      },
      error: (err: unknown) => {
        this.uploading.set(false);
        this.uploadError.set(
          err instanceof HttpErrorResponse && err.status === 409
            ? 'This file has already been uploaded to this workspace.'
            : err instanceof HttpErrorResponse && err.status === 413
              ? 'File is too large. The limit is 25 MB.'
              : err instanceof HttpErrorResponse && err.status === 400
                ? 'This file type is not supported. Use PDF, PNG, JPEG, GIF, TIFF, BMP or WebP.'
                : 'Could not upload the file. Please try again.',
        );
      },
    });
  }

  /** Download the stored file, saving it under its original file name. */
  protected download(doc: DocumentSummary): void {
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    if (!workspaceId || this.downloadingId()) return;
    this.downloadingId.set(doc.id);
    this.api.downloadDocument(workspaceId, doc.id).subscribe({
      next: (blob) => {
        this.downloadingId.set(null);
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = doc.originalFileName;
        a.click();
        URL.revokeObjectURL(url);
      },
      error: () => {
        this.downloadingId.set(null);
        this.error.set('Could not download the document. Please try again.');
      },
    });
  }

  /** Open the stored file in a new tab (PDFs and images render in-browser). */
  protected view(doc: DocumentSummary): void {
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    if (!workspaceId || this.downloadingId()) return;
    this.downloadingId.set(doc.id);
    this.api.downloadDocument(workspaceId, doc.id).subscribe({
      next: (blob) => {
        this.downloadingId.set(null);
        const url = URL.createObjectURL(blob);
        window.open(url, '_blank');
        // Let the new tab load before releasing the object URL.
        window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
      },
      error: () => {
        this.downloadingId.set(null);
        this.error.set('Could not open the document. Please try again.');
      },
    });
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
