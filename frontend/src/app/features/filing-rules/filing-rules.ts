import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, effect, inject, signal } from '@angular/core';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ApiService } from '../../core/api.service';
import {
  Archive,
  FilingRule,
  FilingRuleInput,
  Tag,
} from '../../core/models';
import { WorkspaceService } from '../../core/workspace.service';
import { EmptyState } from '../../shared/empty-state/empty-state';
import { LoadingSpinner } from '../../shared/loading-spinner/loading-spinner';

/** Categories the classifier suggests — a rule can match on, or set, any of them. */
export const FILING_CATEGORIES = [
  'Receipt',
  'Invoice',
  'Contract',
  'BankStatement',
  'Letter',
  'Other',
] as const;

/** Display label for a stored category value. */
export function filingCategoryLabel(category: string | null | undefined): string {
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

const emptyForm = {
  name: '',
  priority: 0,
  isEnabled: true,
  vendorContains: '',
  category: '',
  fileNameContains: '',
  assignTagId: '',
  setCategory: '',
  moveToArchiveId: '',
};

/**
 * Filing rules for the selected workspace (Phase 4 organisation). A rule files
 * documents automatically when they finish OCR: vendor, category or file-name
 * conditions, and assign-a-tag / set-a-category / move-to-an-archive actions.
 * Rules run in priority order; all matching rules apply. The same form creates
 * a rule and edits one (Edit loads the rule into the form).
 */
@Component({
  selector: 'app-filing-rules',
  imports: [DatePipe, ReactiveFormsModule, EmptyState, LoadingSpinner],
  templateUrl: './filing-rules.html',
  styleUrl: './filing-rules.css',
})
export class FilingRules {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);
  protected readonly workspaceService = inject(WorkspaceService);

  protected readonly rules = signal<FilingRule[]>([]);
  protected readonly tags = signal<Tag[]>([]);
  protected readonly archives = signal<Archive[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly formSuccess = signal<string | null>(null);
  protected readonly deletingId = signal<string | null>(null);
  protected readonly togglingId = signal<string | null>(null);
  /** Rule being edited, or null when the form creates a new rule. */
  protected readonly editingId = signal<string | null>(null);

  protected readonly categories = FILING_CATEGORIES;
  protected readonly filingCategoryLabel = filingCategoryLabel;

  protected readonly form = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    priority: [0, [Validators.required, Validators.min(0), Validators.max(10000)]],
    isEnabled: [true],
    vendorContains: ['', [Validators.maxLength(200)]],
    category: [''],
    fileNameContains: ['', [Validators.maxLength(200)]],
    assignTagId: [''],
    setCategory: [''],
    moveToArchiveId: [''],
  });

  constructor() {
    effect(() => {
      const workspaceId = this.workspaceService.selectedWorkspaceId();
      if (workspaceId) {
        this.load(workspaceId);
      } else {
        this.rules.set([]);
        this.tags.set([]);
        this.archives.set([]);
        this.error.set(null);
      }
    });
  }

  /** Human-readable summary of a rule's conditions, for the list. */
  protected describeConditions(rule: FilingRule): string {
    const parts: string[] = [];
    if (rule.vendorContains) parts.push(`vendor contains “${rule.vendorContains}”`);
    if (rule.category) parts.push(`category is ${filingCategoryLabel(rule.category)}`);
    if (rule.fileNameContains) parts.push(`file name contains “${rule.fileNameContains}”`);
    return parts.join(' · ') || '—';
  }

  /** Human-readable summary of a rule's actions, for the list. */
  protected describeActions(rule: FilingRule): string {
    const parts: string[] = [];
    if (rule.assignTagId) {
      const tag = this.tags().find((t) => t.id === rule.assignTagId);
      parts.push(`tag “${tag?.name ?? rule.assignTagId}”`);
    }
    if (rule.setCategory) parts.push(`category → ${filingCategoryLabel(rule.setCategory)}`);
    if (rule.moveToArchiveId) {
      const archive = this.archives().find((a) => a.id === rule.moveToArchiveId);
      parts.push(`move to “${archive?.name ?? rule.moveToArchiveId}”`);
    }
    return parts.join(' · ') || '—';
  }

  protected appliedLabel(rule: FilingRule): string {
    return rule.appliedCount === 0
      ? 'Not applied yet'
      : rule.appliedCount === 1
        ? 'Applied once'
        : `Applied ${rule.appliedCount} times`;
  }

  /** Loads a rule into the form for editing. */
  protected startEdit(rule: FilingRule): void {
    this.editingId.set(rule.id);
    this.formError.set(null);
    this.formSuccess.set(null);
    this.form.setValue({
      name: rule.name,
      priority: rule.priority,
      isEnabled: rule.isEnabled,
      vendorContains: rule.vendorContains ?? '',
      category: rule.category ?? '',
      fileNameContains: rule.fileNameContains ?? '',
      assignTagId: rule.assignTagId ?? '',
      setCategory: rule.setCategory ?? '',
      moveToArchiveId: rule.moveToArchiveId ?? '',
    });
  }

  protected cancelEdit(): void {
    this.editingId.set(null);
    this.formError.set(null);
    this.formSuccess.set(null);
    this.form.reset(emptyForm);
  }

  /** Resets the form for a new rule without clearing the status messages. */
  private resetForm(): void {
    this.editingId.set(null);
    this.form.reset(emptyForm);
  }

  /** Enables or disables a rule without opening the edit form. */
  protected toggleEnabled(rule: FilingRule): void {
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    if (!workspaceId || this.togglingId()) return;
    this.togglingId.set(rule.id);
    this.formError.set(null);
    this.api
      .updateFilingRule(workspaceId, rule.id, toInput(rule, !rule.isEnabled))
      .subscribe({
        next: (updated) => {
          this.togglingId.set(null);
          this.rules.update((list) =>
            list.map((r) => (r.id === updated.id ? updated : r)),
          );
        },
        error: () => {
          this.togglingId.set(null);
          this.formError.set('Could not update the rule. Please try again.');
        },
      });
  }

  protected deleteRule(rule: FilingRule): void {
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    if (!workspaceId || this.deletingId()) return;
    this.deletingId.set(rule.id);
    this.formError.set(null);
    this.api.deleteFilingRule(workspaceId, rule.id).subscribe({
      next: () => {
        this.deletingId.set(null);
        this.rules.update((list) => list.filter((r) => r.id !== rule.id));
        if (this.editingId() === rule.id) this.cancelEdit();
      },
      error: () => {
        this.deletingId.set(null);
        this.formError.set('Could not delete the rule. Please try again.');
      },
    });
  }

  protected onSubmit(): void {
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    if (!workspaceId || this.saving()) return;
    this.formError.set(null);
    this.formSuccess.set(null);
    const raw = this.form.getRawValue();
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.formError.set('Check the form: a name is required (up to 100 characters) and priority must be 0–10000.');
      return;
    }
    const input: FilingRuleInput = {
      name: (raw.name ?? '').trim(),
      isEnabled: raw.isEnabled ?? true,
      priority: raw.priority ?? 0,
      vendorContains: blankToNull(raw.vendorContains),
      category: blankToNull(raw.category),
      fileNameContains: blankToNull(raw.fileNameContains),
      assignTagId: blankToNull(raw.assignTagId),
      setCategory: blankToNull(raw.setCategory),
      moveToArchiveId: blankToNull(raw.moveToArchiveId),
    };
    if (!input.vendorContains && !input.category && !input.fileNameContains) {
      this.formError.set('A rule needs at least one condition: vendor, category or file name.');
      return;
    }
    if (!input.assignTagId && !input.setCategory && !input.moveToArchiveId) {
      this.formError.set('A rule needs at least one action: assign a tag, set a category or move to an archive.');
      return;
    }
    this.saving.set(true);
    const editingId = this.editingId();
    const done = (rule: FilingRule, message: string) => {
      this.saving.set(false);
      this.formSuccess.set(message);
      if (editingId) {
        this.rules.update((list) =>
          list.map((r) => (r.id === rule.id ? rule : r)),
        );
      } else {
        this.rules.update((list) =>
          [...list, rule].sort(
            (a, b) => a.priority - b.priority || a.createdAt.localeCompare(b.createdAt),
          ),
        );
      }
      this.resetForm();
    };
    const failed = (err: unknown) => {
      this.saving.set(false);
      this.formError.set(
        err instanceof HttpErrorResponse && err.status === 400
          ? 'Could not save the rule. Check the name is unique, and that it has at least one condition and one action.'
          : err instanceof HttpErrorResponse && err.status === 404
            ? 'The chosen tag or archive no longer exists in this workspace.'
            : err instanceof HttpErrorResponse && err.status === 403
              ? 'You do not have permission to manage filing rules in this workspace.'
              : 'Could not save the rule. Please try again.',
      );
    };
    if (editingId) {
      this.api.updateFilingRule(workspaceId, editingId, input).subscribe({
        next: (rule) => done(rule, 'Rule updated.'),
        error: failed,
      });
    } else {
      this.api.createFilingRule(workspaceId, input).subscribe({
        next: (rule) => done(rule, 'Rule created.'),
        error: failed,
      });
    }
  }

  private load(workspaceId: string): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.getFilingRules(workspaceId).subscribe({
      next: (list) => {
        this.rules.set(list);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set(
          'Could not load filing rules. Please check your connection and try again.',
        );
      },
    });
    this.api.getTags(workspaceId).subscribe({
      next: (tags) => this.tags.set(tags),
      error: () => this.tags.set([]),
    });
    this.api.getArchives(workspaceId).subscribe({
      next: (archives) => this.archives.set(archives),
      error: () => this.archives.set([]),
    });
  }
}

/** Builds the API payload from a stored rule, optionally flipping its enabled flag. */
function toInput(rule: FilingRule, isEnabled?: boolean): FilingRuleInput {
  return {
    name: rule.name,
    isEnabled: isEnabled ?? rule.isEnabled,
    priority: rule.priority,
    vendorContains: rule.vendorContains,
    category: rule.category,
    fileNameContains: rule.fileNameContains,
    assignTagId: rule.assignTagId,
    setCategory: rule.setCategory,
    moveToArchiveId: rule.moveToArchiveId,
  };
}

function blankToNull(value: string | null | undefined): string | null {
  const trimmed = (value ?? '').trim();
  return trimmed === '' ? null : trimmed;
}
