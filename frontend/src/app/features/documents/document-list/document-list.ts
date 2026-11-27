import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import {
  Component,
  DestroyRef,
  effect,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { ApiService } from '../../../core/api.service';
import { DocumentSummary, MetadataInput, Tag } from '../../../core/models';
import { WorkspaceService } from '../../../core/workspace.service';
import { DocumentViewer } from '../document-viewer/document-viewer';
import { EmptyState } from '../../../shared/empty-state/empty-state';
import { LoadingSpinner } from '../../../shared/loading-spinner/loading-spinner';

/** Draft values for the metadata edit form, keyed by document id. */
interface MetadataDraft {
  fileName: string;
  vendor: string;
  /** yyyy-MM-dd, as the date input wants it. */
  date: string;
  /** Raw text from the number input; empty means "clear the total". */
  total: string;
  /** '' means no confirmed category. */
  category: string;
}

/** Categories the classifier suggests and a reviewer can confirm. */
export const DOCUMENT_CATEGORIES = [
  'Receipt',
  'Invoice',
  'Contract',
  'BankStatement',
  'Letter',
  'Other',
] as const;

/** Display label for a stored category value. */
export function categoryLabel(category: string | null | undefined): string {
  switch (category) {
    case 'BankStatement':
      return 'Bank Statement';
    case null:
    case undefined:
    case '':
      return '—';
    default:
      return category;
  }
}

/**
 * Document list for the selected workspace, with upload, download, OCR
 * details and full-text search over file names and OCR text (Phase 3).
 * Statuses match the backend DocumentStatus enum exactly:
 * Uploaded / Processing / Ready / Failed.
 */
@Component({
  selector: 'app-document-list',
  imports: [DatePipe, DocumentViewer, EmptyState, LoadingSpinner],
  templateUrl: './document-list.html',
  styleUrl: './document-list.css',
})
export class DocumentList {
  private readonly api = inject(ApiService);
  protected readonly workspaceService = inject(WorkspaceService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);

  protected readonly documents = signal<DocumentSummary[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly uploading = signal(false);
  protected readonly uploadError = signal<string | null>(null);
  protected readonly uploadSuccess = signal(false);
  protected readonly downloadingId = signal<string | null>(null);
  /** Phase 3: full-text search term (file name, vendor, OCR text). */
  protected readonly searchTerm = signal('');
  /** Phase 3: document whose OCR details panel is open. */
  protected readonly expandedId = signal<string | null>(null);
  protected readonly reprocessingId = signal<string | null>(null);
  /** Phase 4 filing rules: document whose rules are being applied manually. */
  protected readonly applyingRulesId = signal<string | null>(null);
  protected readonly applyRulesError = signal<string | null>(null);
  /** Phase 6: document open in the in-app viewer, or null when closed. */
  protected readonly viewingDoc = signal<DocumentSummary | null>(null);
  /** Phase 3 classification: show only the human-review queue (from ?review=true). */
  protected readonly reviewOnly = signal(false);
  /** Phase 3 classification: document whose category is being confirmed. */
  protected readonly confirmingId = signal<string | null>(null);
  protected readonly confirmError = signal<string | null>(null);
  /** Reviewer's chosen category per document id (defaults to the suggestion). */
  protected readonly chosenCategory: Record<string, string> = {};
  protected readonly categories = DOCUMENT_CATEGORIES;
  protected readonly categoryLabel = categoryLabel;
  /** Phase 4 organisation: workspace tags, and the tag filter (null = all). */
  protected readonly tags = signal<Tag[]>([]);
  protected readonly selectedTagId = signal<string | null>(null);
  protected readonly newTagName = signal('');
  protected readonly newTagColour = signal('#0e7c66');
  protected readonly tagError = signal<string | null>(null);
  protected readonly creatingTag = signal(false);
  protected readonly deletingTagId = signal<string | null>(null);
  /** Phase 4: document whose tags are being changed (assign/unassign). */
  protected readonly taggingId = signal<string | null>(null);
  protected readonly tagOpError = signal<string | null>(null);
  /** Tag chosen in a document's "Add a tag" dropdown, per document id. */
  protected readonly chosenTag: Record<string, string> = {};

  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private pollCount = 0;
  private readonly maxPolls = 24; // ~2 minutes at 5s, then the row stays as-is

  constructor() {
    this.reviewOnly.set(
      this.route.snapshot.queryParamMap.get('review') === 'true',
    );
    effect(() => {
      const workspaceId = this.workspaceService.selectedWorkspaceId();
      if (workspaceId) {
        this.load(workspaceId);
        this.loadTags(workspaceId);
      } else {
        this.documents.set([]);
        this.tags.set([]);
        this.selectedTagId.set(null);
        this.error.set(null);
      }
    });
    this.destroyRef.onDestroy(() => {
      if (this.searchTimer) clearTimeout(this.searchTimer);
      if (this.pollTimer) clearTimeout(this.pollTimer);
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

  /** OCR confidence as a rounded percent, or null when OCR has not run. */
  protected confidenceLabel(doc: DocumentSummary): string | null {
    return doc.ocrConfidence == null
      ? null
      : `${doc.ocrConfidence.toFixed(1)}%`;
  }

  /** Classification confidence as a whole percent, or null when unclassified. */
  protected classificationLabel(doc: DocumentSummary): string | null {
    return doc.classificationConfidence == null
      ? null
      : `${Math.round(doc.classificationConfidence)}%`;
  }

  /** The category to show: a human-confirmed one wins over the suggestion. */
  protected effectiveCategory(doc: DocumentSummary): string | null {
    return doc.confirmedCategory ?? doc.suggestedCategory ?? null;
  }

  /** Phase 3 classification: show only the human-review queue, or all documents. */
  protected toggleReviewOnly(): void {
    this.reviewOnly.set(!this.reviewOnly());
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    if (workspaceId) this.load(workspaceId);
  }

  /** Phase 3 classification: confirm or correct a document's category. */
  protected confirmCategory(doc: DocumentSummary): void {
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    if (!workspaceId || this.confirmingId()) return;
    const category =
      this.chosenCategory[doc.id] ??
      this.effectiveCategory(doc) ??
      'Other';
    this.confirmingId.set(doc.id);
    this.confirmError.set(null);
    this.api.confirmCategory(workspaceId, doc.id, category).subscribe({
      next: (updated) => {
        this.confirmingId.set(null);
        this.documents.set(
          this.documents().map((d) => (d.id === doc.id ? updated : d)),
        );
      },
      error: () => {
        this.confirmingId.set(null);
        this.confirmError.set(
          'Could not save the category. Please try again.',
        );
      },
    });
  }

  protected onCategoryChosen(doc: DocumentSummary, event: Event): void {
    this.chosenCategory[doc.id] = (event.target as HTMLSelectElement).value;
  }

  /** Phase 4 metadata editing: document whose details are being edited. */
  protected readonly editingId = signal<string | null>(null);
  protected readonly savingMetadataId = signal<string | null>(null);
  protected readonly metadataError = signal<string | null>(null);
  /** Edit-form draft values, keyed by document id. */
  protected readonly metadataDraft: Record<string, MetadataDraft> = {};

  /** Phase 4: open the edit form, pre-filled with the document's values. */
  protected startEditing(doc: DocumentSummary): void {
    this.metadataDraft[doc.id] = {
      fileName: doc.originalFileName,
      vendor: doc.extractedVendor ?? '',
      date: doc.extractedDate ? doc.extractedDate.slice(0, 10) : '',
      total: doc.extractedTotal != null ? String(doc.extractedTotal) : '',
      category: doc.confirmedCategory ?? doc.suggestedCategory ?? '',
    };
    this.metadataError.set(null);
    this.editingId.set(doc.id);
  }

  /** Phase 4: close the edit form without saving. */
  protected cancelEditing(doc: DocumentSummary): void {
    delete this.metadataDraft[doc.id];
    this.metadataError.set(null);
    this.editingId.set(null);
  }

  protected setDraftField(
    doc: DocumentSummary,
    field: keyof MetadataDraft,
    value: string,
  ): void {
    const draft = this.metadataDraft[doc.id];
    if (draft) draft[field] = value;
  }

  /**
   * Phase 4: save the edit form. Full-form PUT — every field is sent; an
   * empty vendor/date/total clears that field, and an empty category clears
   * the confirmed category. A known category counts as confirmed.
   */
  protected saveMetadata(doc: DocumentSummary): void {
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    const draft = this.metadataDraft[doc.id];
    if (!workspaceId || !draft || this.savingMetadataId()) return;
    const fileName = draft.fileName.trim();
    if (!fileName) {
      this.metadataError.set('File name is required.');
      return;
    }
    let total: number | null = null;
    if (draft.total.trim() !== '') {
      total = Number(draft.total);
      if (!Number.isFinite(total) || total < 0) {
        this.metadataError.set('Total must be a number of 0 or more.');
        return;
      }
    }
    this.savingMetadataId.set(doc.id);
    this.metadataError.set(null);
    const input: MetadataInput = {
      originalFileName: fileName,
      extractedVendor: draft.vendor.trim() || null,
      extractedDate: draft.date || null,
      extractedTotal: total,
      category: draft.category || null,
    };
    this.api.updateDocumentMetadata(workspaceId, doc.id, input).subscribe({
      next: (updated) => {
        this.savingMetadataId.set(null);
        this.editingId.set(null);
        delete this.metadataDraft[doc.id];
        this.documents.set(
          this.documents().map((d) => (d.id === doc.id ? updated : d)),
        );
      },
      error: (err: unknown) => {
        this.savingMetadataId.set(null);
        this.metadataError.set(
          err instanceof HttpErrorResponse && err.status === 400
            ? 'Could not save the details. Check the file name, date and total are valid.'
            : 'Could not save the details. Please try again.',
        );
      },
    });
  }

  /** Phase 4 organisation: load the workspace's tags. */
  private loadTags(workspaceId: string): void {
    this.api.getTags(workspaceId).subscribe({
      next: (tags) => this.tags.set(tags),
      error: () => this.tags.set([]),
    });
  }

  /** Phase 4: filter the list to documents carrying one tag (or all). */
  protected onTagFilterChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.selectedTagId.set(value === '' ? null : value);
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    if (workspaceId) this.load(workspaceId);
  }

  /** Phase 4: create a tag in this workspace. */
  protected createTag(): void {
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    const name = this.newTagName().trim();
    if (!workspaceId || this.creatingTag()) return;
    if (!name) {
      this.tagError.set('Tag name is required.');
      return;
    }
    this.creatingTag.set(true);
    this.tagError.set(null);
    this.api.createTag(workspaceId, name, this.newTagColour()).subscribe({
      next: (tag) => {
        this.creatingTag.set(false);
        this.tags.set([...this.tags(), tag].sort((a, b) => a.name.localeCompare(b.name)));
        this.newTagName.set('');
      },
      error: (err: unknown) => {
        this.creatingTag.set(false);
        this.tagError.set(
          err instanceof HttpErrorResponse && err.status === 400
            ? 'Could not create the tag. Check the name is unique and 1–64 characters.'
            : 'Could not create the tag. Please try again.',
        );
      },
    });
  }

  /** Phase 4: delete a tag (and all its document assignments). */
  protected deleteTag(tag: Tag): void {
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    if (!workspaceId || this.deletingTagId()) return;
    this.deletingTagId.set(tag.id);
    this.tagError.set(null);
    this.api.deleteTag(workspaceId, tag.id).subscribe({
      next: () => {
        this.deletingTagId.set(null);
        this.tags.set(this.tags().filter((t) => t.id !== tag.id));
        if (this.selectedTagId() === tag.id) {
          this.selectedTagId.set(null);
          this.load(workspaceId);
        }
      },
      error: () => {
        this.deletingTagId.set(null);
        this.tagError.set('Could not delete the tag. Please try again.');
      },
    });
  }

  /** Phase 4: tags of this workspace not yet on the document. */
  protected availableTags(doc: DocumentSummary): Tag[] {
    const assigned = new Set((doc.tags ?? []).map((t) => t.id));
    return this.tags().filter((t) => !assigned.has(t.id));
  }

  protected onTagChosen(doc: DocumentSummary, event: Event): void {
    this.chosenTag[doc.id] = (event.target as HTMLSelectElement).value;
  }

  /** Phase 4: assign the chosen tag to a document. */
  protected assignTag(doc: DocumentSummary): void {
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    const tagId = this.chosenTag[doc.id];
    if (!workspaceId || !tagId || this.taggingId()) return;
    this.taggingId.set(doc.id);
    this.tagOpError.set(null);
    this.api.assignTag(workspaceId, doc.id, tagId).subscribe({
      next: (tags) => {
        this.taggingId.set(null);
        delete this.chosenTag[doc.id];
        this.updateDocTags(doc.id, tags);
      },
      error: () => {
        this.taggingId.set(null);
        this.tagOpError.set('Could not add the tag. Please try again.');
      },
    });
  }

  /** Phase 4: remove a tag from a document. */
  protected unassignTag(doc: DocumentSummary, tag: Tag): void {
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    if (!workspaceId || this.taggingId()) return;
    this.taggingId.set(doc.id);
    this.tagOpError.set(null);
    this.api.unassignTag(workspaceId, doc.id, tag.id).subscribe({
      next: (tags) => {
        this.taggingId.set(null);
        this.updateDocTags(doc.id, tags);
      },
      error: () => {
        this.taggingId.set(null);
        this.tagOpError.set('Could not remove the tag. Please try again.');
      },
    });
  }

  private updateDocTags(documentId: string, tags: Tag[]): void {
    this.documents.set(
      this.documents().map((d) =>
        d.id === documentId ? { ...d, tags } : d,
      ),
    );
  }

  /** Phase 3: search the workspace's documents (file name, vendor, OCR text). */
  protected onSearchInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.searchTerm.set(value);
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      const workspaceId = this.workspaceService.selectedWorkspaceId();
      if (workspaceId) this.load(workspaceId);
    }, 300);
  }

  /** Phase 3: show or hide a document's extracted OCR fields and text. */
  protected toggleDetails(doc: DocumentSummary): void {
    this.expandedId.set(this.expandedId() === doc.id ? null : doc.id);
  }

  /** Phase 3: re-run OCR on a failed document. */
  protected reprocess(doc: DocumentSummary): void {
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    if (!workspaceId || this.reprocessingId()) return;
    this.reprocessingId.set(doc.id);
    this.uploadError.set(null);
    this.api.reprocessDocument(workspaceId, doc.id).subscribe({
      next: () => {
        this.reprocessingId.set(null);
        this.pollCount = 0;
        this.load(workspaceId);
      },
      error: () => {
        this.reprocessingId.set(null);
        this.uploadError.set('Could not re-run OCR for this document.');
      },
    });
  }

  /**
   * Phase 4 filing rules: manually run the workspace's rules against one
   * document — for example after creating a new rule, to file documents that
   * were uploaded before it existed. The returned document replaces the row.
   */
  protected applyRules(doc: DocumentSummary): void {
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    if (!workspaceId || this.applyingRulesId()) return;
    this.applyingRulesId.set(doc.id);
    this.applyRulesError.set(null);
    this.api.applyFilingRules(workspaceId, doc.id).subscribe({
      next: (updated) => {
        this.applyingRulesId.set(null);
        this.documents.set(
          this.documents().map((d) => (d.id === updated.id ? updated : d)),
        );
      },
      error: () => {
        this.applyingRulesId.set(null);
        this.applyRulesError.set(
          'Could not apply the filing rules. Please try again.',
        );
      },
    });
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

  /**
   * Phase 6: open the document in the in-app viewer (preview endpoint,
   * rendered inline) instead of a new browser tab.
   */
  protected openViewer(doc: DocumentSummary): void {
    this.viewingDoc.set(doc);
  }

  /** Phase 6: close the in-app viewer. */
  protected closeViewer(): void {
    this.viewingDoc.set(null);
  }

  /** Reload the list after a load error — the "Try again" button. */
  protected retryLoad(): void {
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    if (workspaceId) this.load(workspaceId);
  }

  private load(workspaceId: string, silent = false): void {
    if (!silent) {
      this.loading.set(true);
      this.pollCount = 0;
    }
    this.error.set(null);
    this.api
      .getDocuments(
        workspaceId,
        this.searchTerm(),
        this.reviewOnly() || undefined,
        this.selectedTagId() ?? undefined,
      )
      .subscribe({
      next: (docs) => {
        this.documents.set(docs);
        this.loading.set(false);
        this.schedulePoll(workspaceId, docs);
      },
      error: () => {
        this.loading.set(false);
        this.error.set(
          'Could not load documents. Please check your connection and try again.',
        );
      },
    });
  }

  /**
   * Phase 3: while any document is still being processed, reload quietly every
   * 5 seconds so the row flips to Ready (with its OCR fields) without a manual
   * refresh. Stops after ~2 minutes or when nothing is pending.
   */
  private schedulePoll(workspaceId: string, docs: DocumentSummary[]): void {
    if (this.pollTimer) clearTimeout(this.pollTimer);
    const pending = docs.some(
      (d) =>
        d.status === 'Uploaded' ||
        d.status === 'Processing' ||
        d.ocrStatus === 'Queued' ||
        d.ocrStatus === 'Processing',
    );
    if (!pending || this.pollCount >= this.maxPolls) {
      this.pollCount = 0;
      return;
    }
    this.pollCount++;
    this.pollTimer = setTimeout(
      () => this.load(workspaceId, true),
      5000,
    );
  }
}
