import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { WorkspaceService } from '../../../core/workspace.service';
import { EmptyState } from '../../../shared/empty-state/empty-state';
import { LoadingSpinner } from '../../../shared/loading-spinner/loading-spinner';

@Component({
  selector: 'app-workspace-list',
  imports: [DatePipe, ReactiveFormsModule, EmptyState, LoadingSpinner],
  templateUrl: './workspace-list.html',
  styleUrl: './workspace-list.css',
})
export class WorkspaceList {
  private readonly fb = inject(FormBuilder);
  protected readonly workspaceService = inject(WorkspaceService);

  protected readonly creating = signal(false);
  protected readonly createError = signal<string | null>(null);
  protected readonly createSuccess = signal(false);

  protected readonly form = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    description: ['', [Validators.maxLength(500)]],
  });

  protected onCreate(): void {
    if (this.form.invalid || this.creating()) {
      this.form.markAllAsTouched();
      return;
    }
    this.creating.set(true);
    this.createError.set(null);
    this.createSuccess.set(false);
    const { name, description } = this.form.getRawValue();
    this.workspaceService.createWorkspace(name!, description || undefined).subscribe({
      next: () => {
        this.creating.set(false);
        this.createSuccess.set(true);
        this.form.reset();
      },
      error: (err: unknown) => {
        this.creating.set(false);
        this.createError.set(
          err instanceof HttpErrorResponse && err.status === 409
            ? 'A workspace with this name already exists.'
            : 'Could not create the workspace. Please try again.',
        );
      },
    });
  }
}
