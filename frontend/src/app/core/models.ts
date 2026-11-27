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
