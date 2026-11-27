import { computed, inject, Injectable, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { ApiService } from './api.service';
import { Workspace } from './models';

const SELECTED_WORKSPACE_KEY = 'docdigit.selectedWorkspaceId';

/**
 * Workspace list + the currently selected workspace, shared by the shell,
 * dashboard and documents pages. The selection survives reloads via
 * localStorage; it is only an id, never document data.
 */
@Injectable({ providedIn: 'root' })
export class WorkspaceService {
  private readonly api = inject(ApiService);

  readonly workspaces = signal<Workspace[]>([]);
  readonly loading = signal(false);
  readonly selectedWorkspaceId = signal<string | null>(
    localStorage.getItem(SELECTED_WORKSPACE_KEY),
  );

  readonly selectedWorkspace = computed(() => {
    const id = this.selectedWorkspaceId();
    return this.workspaces().find((w) => w.id === id) ?? null;
  });

  /** Load workspaces from the API; keep or default the selection. */
  loadWorkspaces(): Observable<Workspace[]> {
    this.loading.set(true);
    return this.api.getWorkspaces().pipe(
      tap({
        next: (list) => {
          this.workspaces.set(list);
          this.loading.set(false);
          const current = this.selectedWorkspaceId();
          if (!list.some((w) => w.id === current)) {
            this.selectWorkspace(list[0]?.id ?? null);
          }
        },
        error: () => this.loading.set(false),
      }),
    );
  }

  selectWorkspace(id: string | null): void {
    this.selectedWorkspaceId.set(id);
    if (id) {
      localStorage.setItem(SELECTED_WORKSPACE_KEY, id);
    } else {
      localStorage.removeItem(SELECTED_WORKSPACE_KEY);
    }
  }

  createWorkspace(name: string, description?: string): Observable<Workspace> {
    return this.api
      .createWorkspace(name, description)
      .pipe(
        tap((created) => {
          this.workspaces.update((list) => [...list, created]);
          this.selectWorkspace(created.id);
        }),
      );
  }
}
