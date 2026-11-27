import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { Observable, of, throwError } from 'rxjs';
import { ApiService } from '../../../core/api.service';
import {
  DocumentSummary,
  MetadataInput,
  Tag,
  Workspace,
} from '../../../core/models';
import { WorkspaceService } from '../../../core/workspace.service';
import { DocumentList } from './document-list';

// Phase 4 organisation: tags are created per workspace, assigned to
// documents from the details panel, shown as chips on each row, and can
// filter the list. The tag colour is a dot only — the label text stays
// dark ink on white, so contrast never depends on the chosen colour.
describe('DocumentList tags', () => {
  const workspace: Workspace = {
    id: 'ws-1',
    name: 'Test WS',
    description: null,
    role: 'Owner',
    createdAt: '2026-11-27T00:00:00Z',
    createdById: 'u1',
  };

  const tags: Tag[] = [
    {
      id: 't1',
      workspaceId: 'ws-1',
      name: 'Tax 2026',
      colour: '#0e7c66',
      createdAt: '2026-11-27T00:00:00Z',
    },
    {
      id: 't2',
      workspaceId: 'ws-1',
      name: 'Receipts',
      colour: '#2563eb',
      createdAt: '2026-11-27T00:00:00Z',
    },
  ];

  const docWithTag: DocumentSummary = {
    id: 'd1',
    originalFileName: 'receipt.png',
    mimeType: 'image/png',
    fileSizeBytes: 100,
    status: 'Ready',
    ocrStatus: 'Done',
    archiveId: null,
    createdAt: '2026-11-27T00:00:00Z',
    tags: [tags[0]],
  };

  const docWithoutTag: DocumentSummary = {
    ...docWithTag,
    id: 'd2',
    originalFileName: 'letter.pdf',
    mimeType: 'application/pdf',
    tags: [],
  };

  /** Phase 4 metadata editing: a document with extracted fields to correct. */
  const docWithMeta: DocumentSummary = {
    ...docWithTag,
    id: 'd3',
    originalFileName: 'tesco.png',
    extractedVendor: 'Tesco Extra',
    extractedDate: '2026-11-24T00:00:00Z',
    extractedTotal: 47.82,
    extractedCurrency: '£',
    suggestedCategory: 'Receipt',
    ocrText: 'TESCO EXTRA\nTOTAL 47.82',
  };

  let api: {
    getDocuments: ReturnType<typeof vi.fn>;
    getTags: ReturnType<typeof vi.fn>;
    createTag: ReturnType<typeof vi.fn>;
    deleteTag: ReturnType<typeof vi.fn>;
    assignTag: ReturnType<typeof vi.fn>;
    unassignTag: ReturnType<typeof vi.fn>;
    applyFilingRules: ReturnType<typeof vi.fn>;
    updateDocumentMetadata: ReturnType<typeof vi.fn>;
  };

  const create = async (
    docs: DocumentSummary[] = [docWithTag],
    getDocuments: () => Observable<DocumentSummary[]> = () => of(docs),
  ) => {
    api = {
      getDocuments: vi.fn(getDocuments),
      getTags: vi.fn(() => of(tags)),
      createTag: vi.fn((_: string, name: string, colour?: string) =>
        of({
          id: 't-new',
          workspaceId: 'ws-1',
          name,
          colour: colour ?? '#0e7c66',
          createdAt: '2026-11-27T00:00:00Z',
        }),
      ),
      deleteTag: vi.fn(() => of(undefined)),
      assignTag: vi.fn(() => of([tags[0], tags[1]])),
      unassignTag: vi.fn(() => of([])),
      applyFilingRules: vi.fn(() => of({ ...docWithoutTag, tags: [tags[0]] })),
      updateDocumentMetadata: vi.fn(
        (_: string, docId: string, input: MetadataInput) => {
          const base =
            docs.find((d) => d.id === docId) ?? docWithTag;
          return of({
            ...base,
            originalFileName: input.originalFileName,
            extractedVendor: input.extractedVendor,
            extractedDate: input.extractedDate
              ? `${input.extractedDate}T00:00:00Z`
              : null,
            extractedTotal: input.extractedTotal,
            confirmedCategory: input.category,
            suggestedCategory:
              input.category ?? base.suggestedCategory ?? null,
          });
        },
      ),
    };
    const workspaceService = {
      workspaces: signal([workspace]),
      loading: signal(false),
      selectedWorkspaceId: signal('ws-1'),
      selectedWorkspace: signal(workspace),
    };
    const route = {
      snapshot: { queryParamMap: { get: () => null } },
    };
    await TestBed.configureTestingModule({
      imports: [DocumentList],
      providers: [
        { provide: ApiService, useValue: api },
        { provide: WorkspaceService, useValue: workspaceService },
        { provide: ActivatedRoute, useValue: route },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(DocumentList);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    return { fixture, el };
  };

  const setInput = (el: HTMLElement, selector: string, value: string) => {
    const input = el.querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };

  const setSelect = (
    el: HTMLElement,
    selector: string,
    value: string,
  ) => {
    const select = el.querySelector(selector) as HTMLSelectElement;
    select.value = value;
    select.dispatchEvent(new Event('change'));
  };

  it('renders assigned tags as chips on the document row', async () => {
    const { el } = await create();
    expect(el.textContent).toContain('receipt.png');
    expect(el.textContent).toContain('Tax 2026');
  });

  it('the tag filter lists tags and reloads with the chosen tagId', async () => {
    const { fixture, el } = await create();
    const filter = el.querySelector('#tag-filter') as HTMLSelectElement;
    expect(filter).not.toBeNull();
    expect(filter.options.length).toBe(3); // All tags + 2 tags
    api.getDocuments.mockClear();
    setSelect(el, '#tag-filter', 't2');
    fixture.detectChanges();
    expect(api.getDocuments).toHaveBeenCalledWith(
      'ws-1',
      '',
      undefined,
      't2',
    );
  });

  it('creating a tag appends it to the tag list', async () => {
    const { fixture, el } = await create();
    setInput(el, '#new-tag-name', 'Invoices');
    const form = el.querySelector('.tag-create') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    expect(api.createTag).toHaveBeenCalledWith('ws-1', 'Invoices', '#0e7c66');
    expect(el.textContent).toContain('Invoices');
  });

  it('a duplicate tag name shows an error and adds nothing', async () => {
    const { fixture, el } = await create();
    api.createTag.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({ status: 400, statusText: 'Bad Request' }),
      ),
    );
    setInput(el, '#new-tag-name', 'Tax 2026');
    const form = el.querySelector('.tag-create') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    expect(el.textContent).toContain('unique');
    expect(el.querySelectorAll('.tag-list .tag-chip').length).toBe(2);
  });

  it('assigning a tag from the details panel updates the row chips', async () => {
    const { fixture, el } = await create([docWithoutTag]);
    // Expand the details panel, then pick the tag and add it.
    (el.querySelector('.doc-actions .btn') as HTMLButtonElement).click();
    fixture.detectChanges();
    setSelect(el, '#add-tag-d2', 't1');
    fixture.detectChanges(); // re-evaluate the Add button's disabled state
    const addButton = Array.from(el.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === 'Add tag',
    ) as HTMLButtonElement;
    addButton.click();
    fixture.detectChanges();
    expect(api.assignTag).toHaveBeenCalledWith('ws-1', 'd2', 't1');
    // The row itself now carries both tags (the manager lists them too,
    // so assert on the row chips, not page text).
    expect(el.querySelectorAll('td.doc-name .tag-chip').length).toBe(2);
  });

  it('removing a tag from the details panel clears its chip', async () => {
    const { fixture, el } = await create();
    (el.querySelector('.doc-actions .btn') as HTMLButtonElement).click();
    fixture.detectChanges();
    const remove = el.querySelector(
      '[aria-label="Remove tag Tax 2026 from receipt.png"]',
    ) as HTMLButtonElement;
    expect(remove).not.toBeNull();
    remove.click();
    fixture.detectChanges();
    expect(api.unassignTag).toHaveBeenCalledWith('ws-1', 'd1', 't1');
    expect(el.querySelectorAll('.doc-tags .tag-chip').length).toBe(0);
  });

  it('deleting a tag removes it from the manager list', async () => {
    const { fixture, el } = await create();
    const remove = el.querySelector(
      '[aria-label="Delete tag Receipts"]',
    ) as HTMLButtonElement;
    remove.click();
    fixture.detectChanges();
    expect(api.deleteTag).toHaveBeenCalledWith('ws-1', 't2');
    expect(el.textContent).not.toContain('Receipts');
  });

  it('applying filing rules calls the API and refreshes the row', async () => {
    const { fixture, el } = await create([docWithoutTag]);
    const apply = Array.from(el.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === 'Apply filing rules',
    ) as HTMLButtonElement;
    apply.click();
    fixture.detectChanges();
    expect(api.applyFilingRules).toHaveBeenCalledWith('ws-1', 'd2');
    expect(el.querySelectorAll('td.doc-name .tag-chip').length).toBe(1);
  });

  // Phase 4 metadata editing: correct a document's extracted fields from the
  // details panel — file name, vendor, date, total and category.
  it('Edit details opens a form pre-filled with the document values', async () => {
    const { fixture, el } = await create([docWithMeta]);
    (el.querySelector('.doc-actions .btn') as HTMLButtonElement).click();
    fixture.detectChanges();
    const edit = Array.from(el.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === 'Edit details',
    ) as HTMLButtonElement;
    edit.click();
    fixture.detectChanges();
    expect(
      (el.querySelector('#meta-filename-d3') as HTMLInputElement).value,
    ).toBe('tesco.png');
    expect(
      (el.querySelector('#meta-vendor-d3') as HTMLInputElement).value,
    ).toBe('Tesco Extra');
    expect(
      (el.querySelector('#meta-date-d3') as HTMLInputElement).value,
    ).toBe('2026-11-24');
    expect(
      (el.querySelector('#meta-total-d3') as HTMLInputElement).value,
    ).toBe('47.82');
    expect(
      (el.querySelector('#meta-category-d3') as HTMLSelectElement).value,
    ).toBe('Receipt');
  });

  it('saving edited details calls the API and refreshes the row', async () => {
    const { fixture, el } = await create([docWithMeta]);
    (el.querySelector('.doc-actions .btn') as HTMLButtonElement).click();
    fixture.detectChanges();
    const edit = Array.from(el.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === 'Edit details',
    ) as HTMLButtonElement;
    edit.click();
    fixture.detectChanges();
    setInput(el, '#meta-filename-d3', 'weekly-shop.png');
    setInput(el, '#meta-vendor-d3', 'Sainsbury');
    const form = el.querySelector('.metadata-form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    expect(api.updateDocumentMetadata).toHaveBeenCalledWith('ws-1', 'd3', {
      originalFileName: 'weekly-shop.png',
      extractedVendor: 'Sainsbury',
      extractedDate: '2026-11-24',
      extractedTotal: 47.82,
      category: 'Receipt',
    });
    // The row shows the new file name and the form closed.
    expect(el.textContent).toContain('weekly-shop.png');
    expect(el.querySelector('.metadata-form')).toBeNull();
  });

  it('a failed save shows an error and keeps the form open', async () => {
    const { fixture, el } = await create([docWithMeta]);
    api.updateDocumentMetadata.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({ status: 400, statusText: 'Bad Request' }),
      ),
    );
    (el.querySelector('.doc-actions .btn') as HTMLButtonElement).click();
    fixture.detectChanges();
    const edit = Array.from(el.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === 'Edit details',
    ) as HTMLButtonElement;
    edit.click();
    fixture.detectChanges();
    const form = el.querySelector('.metadata-form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    expect(el.textContent).toContain('Check the file name');
    expect(el.querySelector('.metadata-form')).not.toBeNull();
  });

  it('the review-box category select opens pre-filled with the suggested category', async () => {
    const docNeedsReview: DocumentSummary = {
      ...docWithTag,
      id: 'd9',
      originalFileName: 'invoice.pdf',
      suggestedCategory: 'Invoice',
      confirmedCategory: null,
      needsReview: true,
      ocrText: 'INVOICE 123',
    };
    const { fixture, el } = await create([docNeedsReview]);
    (el.querySelector('.doc-actions .btn') as HTMLButtonElement).click();
    fixture.detectChanges();
    const select = el.querySelector(
      '.review-box select',
    ) as HTMLSelectElement;
    expect(select).not.toBeNull();
    // [value] on the select is applied before its @for options exist,
    // so the select would open blank instead of on the suggested
    // category — the same bug the metadata form fixed with [selected].
    expect(select.value).toBe('Invoice');
  });

  it('a failed load shows a Try again button that reloads the list', async () => {
    let calls = 0;
    const { fixture, el } = await create([docWithTag], () => {
      calls++;
      return calls === 1
        ? throwError(() => new HttpErrorResponse({ status: 500 }))
        : of([docWithTag]);
    });
    // First load failed: the error shows with a retry button, no rows.
    expect(el.textContent).toContain('Could not load documents');
    expect(el.textContent).not.toContain('receipt.png');
    const retry = Array.from(el.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === 'Try again',
    ) as HTMLButtonElement;
    expect(retry).not.toBeUndefined();
    retry.click();
    fixture.detectChanges();
    expect(el.textContent).toContain('receipt.png');
    expect(el.textContent).not.toContain('Could not load documents');
  });
});
