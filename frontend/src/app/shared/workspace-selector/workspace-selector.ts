import { Component, inject } from '@angular/core';
import { WorkspaceService } from '../../core/workspace.service';

/** Workspace picker shown in the app shell header. */
@Component({
  selector: 'app-workspace-selector',
  templateUrl: './workspace-selector.html',
  styleUrl: './workspace-selector.css',
})
export class WorkspaceSelector {
  protected readonly workspaceService = inject(WorkspaceService);

  protected onChange(event: Event): void {
    const select = event.target as HTMLSelectElement;
    this.workspaceService.selectWorkspace(select.value || null);
  }
}
