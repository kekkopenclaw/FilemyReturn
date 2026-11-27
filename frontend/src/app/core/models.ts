// Typed models matching the backend API contract exactly.
// Field names mirror the backend DTOs (camelCase JSON): AuthResponse,
// UserDto, WorkspaceDto, ArchiveDto, DocumentDto, DashboardDto, AuditLogDto.
// Do not invent fields here — these mirror the backend responses exactly.

export interface User {
  id: string;
  email: string;
  displayName: string | null;
  isSystemAdmin: boolean;
  createdAt: string;
  mustChangePassword: boolean;
}

export interface AuthResponse {
  accessToken: string;
  /**
   * Always empty for the browser flow: the refresh token is delivered as
   * the `docuarchive.refresh` HttpOnly cookie, never in a response body.
   * The field stays in the contract for non-browser API clients.
   */
  refreshToken: string;
  accessTokenExpiresAt: string;
  user: User;
}

/** POST /api/auth/register returns the created user (UserDto). */
export interface RegisterResponse {
  id: string;
  email: string;
  displayName: string | null;
  isSystemAdmin: boolean;
  createdAt: string;
  mustChangePassword: boolean;
}

export interface ChangePasswordRequest {
  email?: string | null;
  currentPassword: string;
  newPassword: string;
}

export interface Workspace {
  id: string;
  name: string;
  description: string | null;
  role: string;
  createdAt: string;
  createdById: string;
}

export interface Archive {
  id: string;
  workspaceId: string;
  name: string;
  description: string | null;
  createdAt: string;
}

/** A workspace tag (Phase 4 organisation). Colour is always set (explicit or palette default). */
export interface Tag {
  id: string;
  workspaceId: string;
  name: string;
  colour: string;
  createdAt: string;
}

/**
 * A filing rule (Phase 4 organisation). At least one condition
 * (vendorContains, category, fileNameContains) and at least one action
 * (assignTagId, setCategory, moveToArchiveId) are required. Rules run in
 * priority order after OCR finishes; all matching rules apply, and for the
 * single-value actions the last matching rule wins.
 */
export interface FilingRule {
  id: string;
  workspaceId: string;
  name: string;
  isEnabled: boolean;
  priority: number;
  vendorContains: string | null;
  category: string | null;
  fileNameContains: string | null;
  assignTagId: string | null;
  setCategory: string | null;
  moveToArchiveId: string | null;
  appliedCount: number;
  createdAt: string;
}

/** Payload for creating or updating a filing rule. */
export interface FilingRuleInput {
  name: string;
  isEnabled: boolean;
  priority: number;
  vendorContains: string | null;
  category: string | null;
  fileNameContains: string | null;
  assignTagId: string | null;
  setCategory: string | null;
  moveToArchiveId: string | null;
}

/** Payload for editing a document's extracted metadata (Phase 4). Null clears vendor/date/total; category null clears the confirmed category. */
export interface MetadataInput {
  originalFileName: string;
  extractedVendor: string | null;
  extractedDate: string | null;
  extractedTotal: number | null;
  category: string | null;
}

/**
 * A physical storage box (Phase 7 physical archive): the real-world home of
 * paper originals. `code` is the short label code (e.g. "BOX-7K2P9Q") printed
 * on the box and encoded in its QR code; `documentCount` is how many
 * documents currently sit in the box.
 */
export interface PhysicalBox {
  id: string;
  workspaceId: string;
  name: string;
  code: string;
  location: string;
  description: string | null;
  documentCount: number;
  createdAt: string;
}

/** Payload for creating or updating a physical box. */
export interface PhysicalBoxInput {
  name: string;
  location: string;
  description: string | null;
}

/** Backend DocumentStatus enum: Uploaded / Processing / Ready / Failed. */
export type DocumentStatus =
  | 'Uploaded'
  | 'Processing'
  | 'Ready'
  | 'Failed'
  | string;

export interface DocumentSummary {
  id: string;
  originalFileName: string;
  mimeType: string;
  fileSizeBytes: number;
  status: DocumentStatus;
  ocrStatus: string;
  archiveId: string | null;
  createdAt: string;
  // Phase 3 OCR / receipt extraction — null until OCR runs.
  ocrText?: string | null;
  ocrConfidence?: number | null;
  extractedVendor?: string | null;
  extractedDate?: string | null;
  extractedTotal?: number | null;
  extractedCurrency?: string | null;
  suggestedCategory?: string | null;
  // Phase 3 classification — null/false until OCR + classification run.
  classificationConfidence?: number | null;
  classificationReason?: string | null;
  needsReview?: boolean;
  confirmedCategory?: string | null;
  // Phase 4 organisation — tags assigned to this document (empty when none).
  tags?: Tag[];
  // Phase 7 physical archive — the box holding the paper original (null when none).
  physicalBoxId?: string | null;
  physicalBoxName?: string | null;
}

export interface DashboardStats {
  workspaceId: string;
  archiveCount: number;
  documentCount: number;
  memberCount: number;
  documentsReady: number;
  documentsProcessing: number;
  documentsFailed: number;
  ocrPending: number;
  awaitingReview: number;
}

export interface AuditLogEntry {
  id: string;
  workspaceId: string | null;
  userId: string;
  action: string;
  entityType: string | null;
  entityId: string | null;
  details: string | null;
  createdAt: string;
}

// Phase 5 search: one hit is the document plus a short excerpt showing the
// match. `matchedField` is "ocr", "vendor" or "fileName".
export interface SearchResult {
  document: DocumentSummary;
  snippet: string;
  matchedField: string;
}

export interface SearchParams {
  q?: string;
  category?: string;
  vendor?: string;
  from?: string;
  to?: string;
  tagId?: string;
  archiveId?: string;
  needsReview?: boolean;
  status?: string;
}

// Phase 9 monitoring: a read-only health snapshot of the whole system.
// `commit` is the deployed git SHA and is null on local runs; `lastBackupAtUtc`
// / `lastBackupDetails` are null when no backup has ever been downloaded.
export interface DocumentCounts {
  total: number;
  uploaded: number;
  processing: number;
  ready: number;
  failed: number;
  ocrQueued: number;
  ocrProcessing: number;
  ocrFailed: number;
  needsReview: number;
}

export interface MonitoringStatus {
  version: string;
  commit: string | null;
  uptimeSeconds: number;
  databaseProvider: string;
  databaseSizeBytes: number;
  userCount: number;
  workspaceCount: number;
  documentCount: number;
  documentsByStatus: DocumentCounts;
  fileCount: number;
  storageSizeBytes: number;
  failedLoginsLast24h: number;
  lastBackupAtUtc: string | null;
  lastBackupDetails: string | null;
  generatedAtUtc: string;
}

// Phase 8 backup: what a backup would contain right now. `provider` is
// "Sqlite" or "Postgres" — in-app download is SQLite-only.
export interface BackupInfo {
  provider: string;
  databaseSizeBytes: number;
  documentCount: number;
  workspaceCount: number;
  fileCount: number;
  storageSizeBytes: number;
  generatedAtUtc: string;
}
