import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  Archive,
  AuditLogEntry,
  DashboardStats,
  DocumentSummary,
  Tag,
  Workspace,
} from './models';

/** Typed HttpClient wrapper for the Phase 1 backend API. */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  // -- Workspaces ---------------------------------------------------------

  getWorkspaces(): Observable<Workspace[]> {
    return this.http.get<Workspace[]>(`${this.baseUrl}/workspaces`);
  }

  createWorkspace(name: string, description?: string): Observable<Workspace> {
    return this.http.post<Workspace>(`${this.baseUrl}/workspaces`, {
      name,
      description: description || null,
    });
  }

  // -- Archives -----------------------------------------------------------

  getArchives(workspaceId: string): Observable<Archive[]> {
    return this.http.get<Archive[]>(
      `${this.baseUrl}/workspaces/${workspaceId}/archives`,
    );
  }

  createArchive(
    workspaceId: string,
    name: string,
    description?: string,
  ): Observable<Archive> {
    return this.http.post<Archive>(
      `${this.baseUrl}/workspaces/${workspaceId}/archives`,
      { name, description: description || null },
    );
  }

  // -- Documents ----------------------------------------------------------

  /**
   * Lists a workspace's documents. Optional `search` matches file name,
   * extracted vendor or the full OCR text (Phase 3 full-text search).
   * Optional `needsReview` restricts to the human-review queue.
   * Optional `tagId` restricts to documents carrying that tag (Phase 4).
   */
  getDocuments(
    workspaceId: string,
    search?: string,
    needsReview?: boolean,
    tagId?: string,
  ): Observable<DocumentSummary[]> {
    const params = new URLSearchParams();
    if (search && search.trim()) params.set('search', search.trim());
    if (needsReview) params.set('needsReview', 'true');
    if (tagId) params.set('tagId', tagId);
    const query = params.toString() ? `?${params.toString()}` : '';
    return this.http.get<DocumentSummary[]>(
      `${this.baseUrl}/workspaces/${workspaceId}/documents${query}`,
    );
  }

  /** Re-runs OCR on a document (Phase 3 reprocess endpoint). */
  reprocessDocument(
    workspaceId: string,
    documentId: string,
  ): Observable<DocumentSummary> {
    return this.http.post<DocumentSummary>(
      `${this.baseUrl}/workspaces/${workspaceId}/documents/${documentId}/reprocess`,
      {},
    );
  }

  /**
   * Human review: confirms or corrects a document's category (Phase 3
   * classification). Clears the review flag. `category` must be one of
   * Receipt, Invoice, Contract, BankStatement, Letter, Other.
   */
  confirmCategory(
    workspaceId: string,
    documentId: string,
    category: string,
  ): Observable<DocumentSummary> {
    return this.http.post<DocumentSummary>(
      `${this.baseUrl}/workspaces/${workspaceId}/documents/${documentId}/confirm-category`,
      { category },
    );
  }

  /**
   * Upload a document (multipart/form-data, field 'file'; optional
   * ?archiveId=). Returns the created document. The backend rejects exact
   * duplicates per workspace with 409 and oversized files with 413.
   */
  uploadDocument(
    workspaceId: string,
    file: File,
    archiveId?: string,
  ): Observable<DocumentSummary> {
    const form = new FormData();
    form.append('file', file, file.name);
    const query = archiveId ? `?archiveId=${encodeURIComponent(archiveId)}` : '';
    return this.http.post<DocumentSummary>(
      `${this.baseUrl}/workspaces/${workspaceId}/documents${query}`,
      form,
    );
  }

  /**
   * Download a document's stored bytes as a Blob. The backend sets the
   * original file name in Content-Disposition; callers should use
   * doc.originalFileName as the save name.
   */
  downloadDocument(workspaceId: string, documentId: string): Observable<Blob> {
    return this.http.get(
      `${this.baseUrl}/workspaces/${workspaceId}/documents/${documentId}/download`,
      { responseType: 'blob' },
    );
  }

  /**
   * Preview a document in the browser (Phase 6 viewer). Same bytes as the
   * download, but the backend serves them with Content-Disposition: inline
   * and audits the view as Document.Viewed. Only PDF, PNG, JPEG, GIF and
   * WebP render inline — TIFF and BMP return 400, so callers must offer a
   * download instead. Fetched through HttpClient (not a raw link) so the
   * Bearer token travels with the request.
   */
  previewDocument(workspaceId: string, documentId: string): Observable<Blob> {
    return this.http.get(
      `${this.baseUrl}/workspaces/${workspaceId}/documents/${documentId}/preview`,
      { responseType: 'blob' },
    );
  }

  // -- Tags (Phase 4 organisation) ----------------------------------------

  /** Lists a workspace's tags, in name order. */
  getTags(workspaceId: string): Observable<Tag[]> {
    return this.http.get<Tag[]>(
      `${this.baseUrl}/workspaces/${workspaceId}/tags`,
    );
  }

  /**
   * Creates a tag. `colour` is an optional #RRGGBB hex value — the backend
   * picks a deterministic palette colour when omitted. 400 on a duplicate
   * name or a bad colour.
   */
  createTag(
    workspaceId: string,
    name: string,
    colour?: string,
  ): Observable<Tag> {
    return this.http.post<Tag>(
      `${this.baseUrl}/workspaces/${workspaceId}/tags`,
      { name, colour: colour || null },
    );
  }

  /** Deletes a tag and all its document assignments. */
  deleteTag(workspaceId: string, tagId: string): Observable<void> {
    return this.http.delete<void>(
      `${this.baseUrl}/workspaces/${workspaceId}/tags/${tagId}`,
    );
  }

  /** Tags assigned to one document, in name order. */
  getDocumentTags(workspaceId: string, documentId: string): Observable<Tag[]> {
    return this.http.get<Tag[]>(
      `${this.baseUrl}/workspaces/${workspaceId}/documents/${documentId}/tags`,
    );
  }

  /**
   * Assigns a tag to a document. Idempotent — assigning a tag that is
   * already there returns the current tag list.
   */
  assignTag(
    workspaceId: string,
    documentId: string,
    tagId: string,
  ): Observable<Tag[]> {
    return this.http.post<Tag[]>(
      `${this.baseUrl}/workspaces/${workspaceId}/documents/${documentId}/tags`,
      { tagId },
    );
  }

  /** Removes a tag from a document. Idempotent. Returns the remaining tags. */
  unassignTag(
    workspaceId: string,
    documentId: string,
    tagId: string,
  ): Observable<Tag[]> {
    return this.http.delete<Tag[]>(
      `${this.baseUrl}/workspaces/${workspaceId}/documents/${documentId}/tags/${tagId}`,
    );
  }

  // -- Dashboard ----------------------------------------------------------

  getDashboard(workspaceId: string): Observable<DashboardStats> {
    return this.http.get<DashboardStats>(
      `${this.baseUrl}/workspaces/${workspaceId}/dashboard`,
    );
  }

  // -- Audit logs ---------------------------------------------------------

  getAuditLogs(workspaceId: string): Observable<AuditLogEntry[]> {
    return this.http.get<AuditLogEntry[]>(
      `${this.baseUrl}/workspaces/${workspaceId}/audit-logs`,
    );
  }
}
