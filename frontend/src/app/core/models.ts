// Typed models matching the Phase 1 backend API contract.
// Do not invent fields here — these mirror the backend responses exactly.

export interface User {
  id: string;
  email: string;
  displayName: string;
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: User;
}

export interface RegisterResponse {
  userId: string;
  email: string;
  displayName: string;
}

export interface Workspace {
  id: string;
  name: string;
  description: string | null;
  role: string;
  createdAt: string;
}

export interface Archive {
  id: string;
  name: string;
  description: string | null;
  createdAt: string;
}

export type DocumentStatus =
  | 'Pending'
  | 'Processing'
  | 'Processed'
  | 'AwaitingReview'
  | 'Failed'
  | string;

export interface DocumentSummary {
  id: string;
  originalFileName: string;
  mimeType: string;
  fileSizeBytes: number;
  status: DocumentStatus;
  createdAt: string;
}

export interface DashboardStats {
  totalDocuments: number;
  processedDocuments: number;
  awaitingReview: number;
  failedJobs: number;
  recentUploads: DocumentSummary[];
  storageBytes: number;
  archiveCount: number;
}

export interface AuditLogEntry {
  id: string;
  action: string;
  entityType: string;
  entityId: string;
  details: string;
  createdAt: string;
  userEmail: string;
}
