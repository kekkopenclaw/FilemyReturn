import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  HostListener,
  inject,
  input,
  OnInit,
  output,
  signal,
} from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { ApiService } from '../../../core/api.service';
import { DocumentSummary } from '../../../core/models';
import { LoadingSpinner } from '../../../shared/loading-spinner/loading-spinner';

/**
 * In-app document viewer (Phase 6). Fetches the file through the backend
 * preview endpoint — Content-Disposition: inline, audited as
 * Document.Viewed — and renders it inside a modal: PDFs in an iframe,
 * images with an <img>. The request goes through HttpClient so the Bearer
 * token travels with it; a raw link or new tab would not be authenticated.
 *
 * TIFF and BMP cannot render in a browser: the backend answers 400 for
 * them, and the viewer says so plainly and offers Download instead.
 */
@Component({
  selector: 'app-document-viewer',
  imports: [LoadingSpinner],
  templateUrl: './document-viewer.html',
  styleUrl: './document-viewer.css',
})
export class DocumentViewer implements OnInit {
  private readonly api = inject(ApiService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly destroyRef = inject(DestroyRef);

  /** Workspace the document belongs to (preview is workspace-scoped). */
  readonly workspaceId = input.required<string>();
  /** Document to preview; its mimeType decides PDF vs image rendering. */
  readonly doc = input.required<DocumentSummary>();
  /** Emitted when the viewer is closed (button, backdrop or Escape). */
  readonly closed = output<void>();

  protected readonly loading = signal(true);
  /** Trusted blob URL for the <iframe> / <img> once the preview loads. */
  protected readonly previewUrl = signal<SafeResourceUrl | null>(null);
  /** True when the file is a PDF; false when it is a previewable image. */
  protected readonly isPdf = signal(false);
  /** True when the backend refused the preview (TIFF/BMP, HTTP 400). */
  protected readonly notPreviewable = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly downloading = signal(false);
  protected readonly downloadError = signal<string | null>(null);

  private objectUrl: string | null = null;

  constructor() {
    this.destroyRef.onDestroy(() => this.revokeObjectUrl());
  }

  ngOnInit(): void {
    this.isPdf.set(this.doc().mimeType === 'application/pdf');
    this.api.previewDocument(this.workspaceId(), this.doc().id).subscribe({
      next: (blob) => {
        this.objectUrl = URL.createObjectURL(blob);
        this.previewUrl.set(
          this.sanitizer.bypassSecurityTrustResourceUrl(this.objectUrl),
        );
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        if (err instanceof HttpErrorResponse && err.status === 400) {
          // TIFF/BMP: the backend refuses inline preview for these types.
          this.notPreviewable.set(true);
        } else {
          this.error.set(
            'Could not load the preview. Please try again or download the file.',
          );
        }
      },
    });
  }

  /** Escape closes the viewer. */
  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    this.close();
  }

  protected close(): void {
    this.closed.emit();
  }

  /** Backdrop click closes; clicks inside the dialog do not. */
  protected onBackdropClick(event: MouseEvent): void {
    if (event.target === event.currentTarget) {
      this.close();
    }
  }

  /** Download the stored file, saving it under its original file name. */
  protected download(): void {
    if (this.downloading()) return;
    const doc = this.doc();
    this.downloading.set(true);
    this.downloadError.set(null);
    this.api.downloadDocument(this.workspaceId(), doc.id).subscribe({
      next: (blob) => {
        this.downloading.set(false);
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = doc.originalFileName;
        a.click();
        URL.revokeObjectURL(url);
      },
      error: () => {
        this.downloading.set(false);
        this.downloadError.set('Could not download the document.');
      },
    });
  }

  private revokeObjectUrl(): void {
    if (this.objectUrl) {
      URL.revokeObjectURL(this.objectUrl);
      this.objectUrl = null;
    }
  }
}
