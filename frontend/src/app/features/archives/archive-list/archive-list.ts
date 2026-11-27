import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, effect, inject, signal } from '@angular/core';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ApiService } from '../../../core/api.service';
import { Archive } from '../../../core/models';
import { WorkspaceService } from '../../../core/workspace.service';
import { EmptyState } from '../../../shared/empty-state/empty-state';
import { LoadingSpinner } from '../../../shared/loading-spinner/loading-spinner';

/** Archives in the selected workspace, with a create form. */
@Component({
  selector: 'app-archive-list',
  imports: [DatePipe, ReactiveFormsModule, EmptyState, LoadingSpinner],
  templateUrl: './archive-list.html',
  styleUrl: './archive-list.css',
})
export class ArchiveList {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);
  protected readonly workspaceService = inject(WorkspaceService);

  protected readonly archives = signal<Archive[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly creating = signal(false);
  protected readonly createError = signal<string | null>(null);
  protected readonly createSuccess = signal(false);

  protected readonly form = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    description: ['', [Validators.maxLength(1000)]],
  });

  constructor() {
    effect(() => {
      const workspaceId = this.workspaceService.selectedWorkspaceId();
      if (workspaceId) {
        this.load(workspaceId);
      } else {
        this.archives.set([]);
        this.error.set(null);
      }
    });
  }

  protected onCreate(): void {
    if (this.form.invalid || this.creating()) {
      this.form.markAllAsTouched();
      return;
    }
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    if (!workspaceId) return;
    this.creating.set(true);
    this.createError.set(null);
    this.createSuccess.set(false);
    const { name, description } = this.form.getRawValue();
    this.api
      .createArchive(workspaceId, name!, description || undefined)
      .subscribe({
        next: (created) => {
          this.creating.set(false);
          this.createSuccess.set(true);
          this.archives.update((list) => [...list, created]);
          this.form.reset();
        },
        error: (err: unknown) => {
          this.creating.set(false);
          this.createError.set(
            err instanceof HttpErrorResponse && err.status === 403
              ? 'You do not have permission to create archives in this workspace.'
              : 'Could not create the archive. Please try again.',
          );
        },
      });
  }

  private load(workspaceId: string): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.getArchives(workspaceId).subscribe({
      next: (list) => {
        this.archives.set(list);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set(
          'Could not load archives. Please check your connection and try again.',
        );
      },
    });
  }
}
