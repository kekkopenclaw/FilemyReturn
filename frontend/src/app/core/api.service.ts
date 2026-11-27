import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  Archive,
  AuditLogEntry,
  DashboardStats,
  DocumentSummary,
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

  // -- Documents ----------------------------------------------------------

  getDocuments(workspaceId: string): Observable<DocumentSummary[]> {
    return this.http.get<DocumentSummary[]>(
      `${this.baseUrl}/workspaces/${workspaceId}/documents`,
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
