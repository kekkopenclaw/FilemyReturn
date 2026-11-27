import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../../core/api.service';
import {
  Archive,
  SearchParams,
  SearchResult,
  Tag,
} from '../../../core/models';
import { WorkspaceService } from '../../../core/workspace.service';
import { EmptyState } from '../../../shared/empty-state/empty-state';
import { LoadingSpinner } from '../../../shared/loading-spinner/loading-spinner';

/** Categories the classifier suggests — the same list as the document list. */
export const SEARCH_CATEGORIES = [
  'Receipt',
  'Invoice',
  'Contract',
  'BankStatement',
  'Letter',
  'Other',
] as const;

/** Display label for a stored category value. */
export function searchCategoryLabel(category: string | null | undefined): string {
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

/** One piece of a snippet: plain text, or a matched query term. */
export interface SnippetSegment {
  text: string;
  match: boolean;
}

/**
 * Phase 5 search: free text over file name, vendor and OCR text plus
 * filters (category, vendor, extracted-date range, tag, archive, review
 * queue, status). Results show a snippet around the match with the query
 * terms highlighted. The snippet is rendered as segments, never as HTML,
 * so a document's text cannot inject markup.
 */
@Component({
  selector: 'app-search',
  imports: [DatePipe, RouterLink, EmptyState, LoadingSpinner],
  templateUrl: './search.html',
  styleUrl: './search.css',
})
export class Search {
  private readonly api = inject(ApiService);
  protected readonly workspaceService = inject(WorkspaceService);

  protected readonly categories = SEARCH_CATEGORIES;
  protected readonly categoryLabel = searchCategoryLabel;
  protected readonly statuses = ['Uploaded', 'Processing', 'Ready', 'Failed'];

  protected readonly q = signal('');
  protected readonly category = signal('');
  protected readonly vendor = signal('');
  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly tagId = signal('');
  protected readonly archiveId = signal('');
  protected readonly needsReview = signal(false);
  protected readonly status = signal('');

  protected readonly tags = signal<Tag[]>([]);
  protected readonly archives = signal<Archive[]>([]);
  protected readonly results = signal<SearchResult[]>([]);
  protected readonly searched = signal(false);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      const workspaceId = this.workspaceService.selectedWorkspaceId();
      if (workspaceId) {
        this.loadFilters(workspaceId);
      } else {
        this.tags.set([]);
        this.archives.set([]);
      }
    });
  }

  protected onSubmit(event: Event): void {
    event.preventDefault();
    this.search();
  }

  /** Runs the search. At least one criterion is required — the API 400s otherwise. */
  protected search(): void {
    const workspaceId = this.workspaceService.selectedWorkspaceId();
    if (!workspaceId) return;
    const params: SearchParams = {
      q: this.q().trim() || undefined,
      category: this.category() || undefined,
      vendor: this.vendor().trim() || undefined,
      from: this.from() || undefined,
      to: this.to() || undefined,
      tagId: this.tagId() || undefined,
      archiveId: this.archiveId() || undefined,
      needsReview: this.needsReview() || undefined,
      status: this.status() || undefined,
    };
    if (Object.values(params).every((v) => v === undefined)) {
      this.error.set(
        'Give the search something to look for: text or at least one filter.',
      );
      return;
    }
    this.loading.set(true);
    this.error.set(null);
    this.api.searchDocuments(workspaceId, params).subscribe({
      next: (results) => {
        this.loading.set(false);
        this.results.set(results);
        this.searched.set(true);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.error.set(
          err instanceof HttpErrorResponse && err.status === 400
            ? 'Check the search: the category, status or dates look wrong.'
            : 'Could not search. Please try again.',
        );
      },
    });
  }

  /** Clears the form and the results. */
  protected clear(): void {
    this.q.set('');
    this.category.set('');
    this.vendor.set('');
    this.from.set('');
    this.to.set('');
    this.tagId.set('');
    this.archiveId.set('');
    this.needsReview.set(false);
    this.status.set('');
    this.results.set([]);
    this.searched.set(false);
    this.error.set(null);
  }

  /** The query terms, for highlighting inside snippets. */
  protected queryTerms(): string[] {
    return this.q()
      .split(/\s+/)
      .map((t) => t.trim())
      .filter((t) => t.length > 0);
  }

  /**
   * Splits a snippet into plain and matched segments, case-insensitively.
   * Terms are regex-escaped, so a query like "C++" cannot break the match.
   */
  protected highlightSegments(snippet: string): SnippetSegment[] {
    const terms = this.queryTerms();
    if (terms.length === 0) return [{ text: snippet, match: false }];
    const escaped = terms.map((t) =>
      t.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'),
    );
    const pattern = new RegExp(`(${escaped.join('|')})`, 'gi');
    return snippet
      .split(pattern)
      .filter((part) => part.length > 0)
      .map((part) => ({
        text: part,
        match: terms.some((t) => t.toLowerCase() === part.toLowerCase()),
      }));
  }

  /** Human label for the field the match came from. */
  protected matchedFieldLabel(field: string): string {
    switch (field) {
      case 'ocr':
        return 'OCR text';
      case 'vendor':
        return 'Vendor';
      default:
        return 'File name';
    }
  }

  /** Effective category: a human-confirmed one wins over the suggestion. */
  protected effectiveCategory(result: SearchResult): string | null {
    const doc = result.document;
    return doc.confirmedCategory ?? doc.suggestedCategory ?? null;
  }

  private loadFilters(workspaceId: string): void {
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
