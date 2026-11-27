import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, effect, inject, SecurityContext, signal } from '@angular/core';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { ActivatedRoute } from '@angular/router';
import { ApiService } from '../../core/api.service';
import {
  DocumentSummary,
  PhysicalBox,
  PhysicalBoxInput,
} from '../../core/models';
import { WorkspaceService } from '../../core/workspace.service';
import { EmptyState } from '../../shared/empty-state/empty-state';
import { LoadingSpinner } from '../../shared/loading-spinner/loading-spinner';

const emptyForm = {
  name: '',
  location: '',
  description: '',
};

/**
 * Builds the printable label for a box: name, code, location and the QR code
 * (inline SVG). Pure function, so the label content is unit-testable without
 * opening a window. The box code is HTML-escaped — it is server-generated,
 * but a label must never inject markup.
 */
export function buildBoxLabelHtml(box: PhysicalBox, qrSvg: string): string {
  const escape = (value: string): string =>
    value
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  return `<!doctype html><html><head><meta charset="utf-8"><title>Box label — ${escape(box.code)}</title><style>
body { font-family: system-ui, sans-serif; text-align: center; padding: 24px; color: #101a17; }
h1 { font-size: 28px; margin: 0 0 4px; }
.code { font-family: monospace; font-size: 20px; font-weight: 700; letter-spacing: 1px; }
.location { font-size: 16px; margin: 8px 0 16px; color: #3d4a45; }
.qr { width: 220px; height: 220px; margin: 0 auto; }
.qr svg { width: 100%; height: 100%; }
.hint { font-size: 12px; color: #6b7a74; margin-top: 16px; }
</style></head><body>
<h1>${escape(box.name)}</h1>
<p class="code">${escape(box.code)}</p>
<p class="location">${escape(box.location)}</p>
<div class="qr">${qrSvg}</div>
<p class="hint">Scan the code to open this box in DocuArchive.</p>
</body></html>`;
}

/**
 * Physical boxes for the selected workspace (Phase 7 physical archive). A box
 * is the real-world home of paper originals: a name, a physical location, and
 * a short printed code. The same form creates a box and edits one (Edit loads
 * the box into the form). Selecting a box shows its documents and its QR
 * code, which can be printed as a label. Opening `/boxes?box=BOX-XXXXXX`
 * (the QR deep link) selects that box directly.
 */
@Component({
  selector: 'app-physical-boxes',
  imports: [DatePipe, ReactiveFormsModule, EmptyState, LoadingSpinner],
  templateUrl: './physical-boxes.html',
  styleUrl: './physical-boxes.css',
})
export class PhysicalBoxes {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly route = inject(ActivatedRoute);
  protected readonly workspaceService = inject(WorkspaceService);

  protected readonly boxes = signal<PhysicalBox[]>([]);
  /** Box whose contents and QR code are shown, or null. */
  protected readonly selectedBoxId = signal<string | null>(null);
  protected readonly boxDocuments = signal<DocumentSummary[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly loadingContents = signal(false);
  protected readonly contentsError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly formSuccess = signal<string | null>(null);
  protected readonly deletingId = signal<string | null>(null);
  /** Box being edited, or null when the form creates a new box. */
  protected readonly editingId = signal<string | null>(null);
  /** The selected box's QR code as trusted HTML (SVG from our own server). */
  protected readonly qrSvg = signal<SafeHtml | null>(null);
  protected readonly qrError = signal<string | null>(null);

  protected readonly form = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    location: ['', [Validators.required, Validators.maxLength(200)]],
    description: ['', [Validators.maxLength(1000)]],
  });

  constructor() {
    effect(() => {
      const workspaceId = this.workspaceService.selectedWorkspaceId();
      if (workspaceId) {
        this.load(workspaceId);
      } else {
        this.boxes.set([]);
        this.selectedBoxId.set(null);
        this.boxDocuments.set([]);
        this.qrSvg.set(null);
        this.error.set(null);
      }
    });
  }

  protected selectedBox(): PhysicalBox | null {
    const id = this.selectedBoxId();
    return id ? (this.boxes().find((b) => b.id === id) ?? null) : null;
  }

  protected documentCountLabel(box: PhysicalBox): string {
    return box.documentCount === 0
      ? 'Empty'
      : box.documentCount === 1
        ? '1 document'
        : `${box.documentCount} documents`;
  }

  /** Shows a box's contents and QR code. */
  protected selectBox(box: PhysicalBox): void {
    this.selectedBoxId.set(box.id);
    this.loadContents(box);
  }

  protected closeBox(): void {
    this.selectedBoxId.set(null);
    this.boxDocuments.set([]);
    this.qrSvg.set(null);
    this.qrError.set(null);
  }

  /** Opens the printable label in a new window and prints it. */
  protected printLabel(): void {
    const box = this.selectedBox();
    const qrMarkup = this.qrMarkup();
    if (!box || !qrMarkup) return;
    const win = window.open('', '_blank');
    if (!win) return;
    win.document.write(buildBoxLabelHtml(box, qrMarkup));
    win.document.close();
    win.focus();
    win.print();
  }

  /** Raw QR markup for the label (kept separate so tests can assert on it). */
  private qrMarkup(): string {
    const svg = this.qrSvg();
    return svg ? (this.sanitizer.sanitize(SecurityContext.HTML, svg) ?? '') : '';
  }

  /** Loads a box into the form for editing. */
  protected startEdit(box: PhysicalBox): void {
    this.editingId.set(box.id);
    this.formError.set(null);
    this.formSuccess.set(null);
    this.form.setValue({
      name: box.name,
      location: box.location,
      description: box.description ?? '',
    });
  }

  protected cancelEdit(): void {
    this.editingId.set(null);
    this.formError.set(null);
    this.formSuccess.set(null);
    this.form.reset(emptyForm);
  }

  /** Resets the form for a new box without clearing the status messages. */
  private resetForm(): void {
    this.editingId.set(null);
    this.form.reset(emptyForm);
  }

  protected deleteBox(box: PhysicalBox): void {
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    if (!workspaceId || this.deletingId()) return;
    this.deletingId.set(box.id);
    this.formError.set(null);
    this.api.deletePhysicalBox(workspaceId, box.id).subscribe({
      next: () => {
        this.deletingId.set(null);
        this.boxes.update((list) => list.filter((b) => b.id !== box.id));
        if (this.editingId() === box.id) this.cancelEdit();
        if (this.selectedBoxId() === box.id) this.closeBox();
        this.formSuccess.set(`Box “${box.name}” deleted. Its documents were kept — they just have no box now.`);
      },
      error: () => {
        this.deletingId.set(null);
        this.formError.set('Could not delete the box. Please try again.');
      },
    });
  }

  protected onSubmit(): void {
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    if (!workspaceId || this.saving()) return;
    this.formError.set(null);
    this.formSuccess.set(null);
    const raw = this.form.getRawValue();
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.formError.set('Check the form: a name is required (up to 100 characters) and a location is required (up to 200 characters).');
      return;
    }
    const input: PhysicalBoxInput = {
      name: (raw.name ?? '').trim(),
      location: (raw.location ?? '').trim(),
      description: blankToNull(raw.description),
    };
    this.saving.set(true);
    const editingId = this.editingId();
    const done = (box: PhysicalBox, message: string) => {
      this.saving.set(false);
      this.formSuccess.set(message);
      if (editingId) {
        this.boxes.update((list) =>
          list.map((b) => (b.id === box.id ? box : b)),
        );
      } else {
        this.boxes.update((list) =>
          [...list, box].sort((a, b) => a.name.localeCompare(b.name)),
        );
      }
      this.resetForm();
    };
    const failed = (err: unknown) => {
      this.saving.set(false);
      this.formError.set(
        err instanceof HttpErrorResponse && err.status === 400
          ? 'Could not save the box. Check the name is unique in this workspace.'
          : err instanceof HttpErrorResponse && err.status === 403
            ? 'You do not have permission to manage physical boxes in this workspace.'
            : 'Could not save the box. Please try again.',
      );
    };
    if (editingId) {
      this.api.updatePhysicalBox(workspaceId, editingId, input).subscribe({
        next: (box) => done(box, 'Box updated.'),
        error: failed,
      });
    } else {
      this.api.createPhysicalBox(workspaceId, input).subscribe({
        next: (box) => done(box, `Box created — its label code is ${box.code}.`),
        error: failed,
      });
    }
  }

  private load(workspaceId: string): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.getPhysicalBoxes(workspaceId).subscribe({
      next: (list) => {
        this.boxes.set(list);
        this.loading.set(false);
        // QR deep link: /boxes?box=BOX-XXXXXX selects that box directly.
        const code = this.route.snapshot.queryParamMap.get('box');
        if (code) {
          const match = list.find((b) => b.code === code);
          if (match) this.selectBox(match);
        }
      },
      error: () => {
        this.loading.set(false);
        this.error.set(
          'Could not load physical boxes. Please check your connection and try again.',
        );
      },
    });
  }

  private loadContents(box: PhysicalBox): void {
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    if (!workspaceId) return;
    this.loadingContents.set(true);
    this.contentsError.set(null);
    this.qrSvg.set(null);
    this.qrError.set(null);
    this.api.getPhysicalBoxDocuments(workspaceId, box.id).subscribe({
      next: (docs) => {
        this.boxDocuments.set(docs);
        this.loadingContents.set(false);
      },
      error: () => {
        this.loadingContents.set(false);
        this.contentsError.set('Could not load this box’s documents. Please try again.');
      },
    });
    this.api.getPhysicalBoxQr(workspaceId, box.id).subscribe({
      next: (svg) => {
        this.qrSvg.set(this.sanitizer.bypassSecurityTrustHtml(svg));
      },
      error: () => {
        this.qrError.set('Could not load the QR code. Please try again.');
      },
    });
  }
}

function blankToNull(value: string | null | undefined): string | null {
  const trimmed = (value ?? '').trim();
  return trimmed === '' ? null : trimmed;
}
