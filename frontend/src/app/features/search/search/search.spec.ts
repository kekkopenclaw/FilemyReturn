import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { of, throwError } from 'rxjs';
import { ApiService } from '../../../core/api.service';
import {
  DocumentSummary,
  SearchResult,
  Workspace,
} from '../../../core/models';
import { WorkspaceService } from '../../../core/workspace.service';
import { Search } from './search';

// Phase 5 search: free text over file name, vendor and OCR text plus
// filters, with the matched terms highlighted in the snippet. The snippet
// is rendered as segments, never as HTML, so document text cannot inject
// markup.
describe('Search page', () => {
  const workspace: Workspace = {
    id: 'ws-1',
    name: 'Test WS',
    description: null,
    role: 'Owner',
    createdAt: '2026-11-27T00:00:00Z',
    createdById: 'u1',
  };

  const doc: DocumentSummary = {
    id: 'd1',
    originalFileName: 'receipt.png',
    mimeType: 'image/png',
    fileSizeBytes: 100,
    status: 'Ready',
    ocrStatus: 'Done',
    archiveId: null,
    createdAt: '2026-11-27T00:00:00Z',
    extractedVendor: 'Corner Cafe',
    suggestedCategory: 'Receipt',
    ocrText: 'THE CORNER CAFE\nflat white 3.20',
  };

  const result: SearchResult = {
    document: doc,
    snippet: 'THE CORNER CAFE flat white 3.20',
    matchedField: 'ocr',
  };

  let api: {
    searchDocuments: ReturnType<typeof vi.fn>;
    getTags: ReturnType<typeof vi.fn>;
    getArchives: ReturnType<typeof vi.fn>;
  };

  const create = async () => {
    api = {
      searchDocuments: vi.fn(() => of([result])),
      getTags: vi.fn(() => of([])),
      getArchives: vi.fn(() => of([])),
    };
    const workspaceService = {
      workspaces: signal([workspace]),
      loading: signal(false),
      selectedWorkspaceId: signal('ws-1'),
      selectedWorkspace: signal(workspace),
    };
    await TestBed.configureTestingModule({
      imports: [Search],
      providers: [
        provideRouter([]),
        { provide: ApiService, useValue: api },
        { provide: WorkspaceService, useValue: workspaceService },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(Search);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    return { fixture, el };
  };

  const setInput = (el: HTMLElement, selector: string, value: string) => {
    const input = el.querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };

  const submit = (fixture: { detectChanges: () => void }, el: HTMLElement) => {
    (el.querySelector('.search-card') as HTMLFormElement).dispatchEvent(
      new Event('submit'),
    );
    fixture.detectChanges();
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('searching with text calls the API and highlights the terms in the snippet', async () => {
    const { fixture, el } = await create();
    setInput(el, '#search-q', '  flat white  ');
    submit(fixture, el);

    expect(api.searchDocuments).toHaveBeenCalledWith('ws-1', {
      q: 'flat white',
      category: undefined,
      vendor: undefined,
      from: undefined,
      to: undefined,
      tagId: undefined,
      archiveId: undefined,
      needsReview: undefined,
      status: undefined,
    });
    expect(el.textContent).toContain('1 document found');
    expect(el.textContent).toContain('receipt.png');
    const marks = Array.from(el.querySelectorAll('mark')).map((m) =>
      m.textContent?.toLowerCase(),
    );
    expect(marks).toEqual(['flat', 'white']);
    expect(el.textContent).toContain('Matched in OCR text');
  });

  it('searching with no criteria shows an error and does not call the API', async () => {
    const { fixture, el } = await create();
    submit(fixture, el);

    expect(api.searchDocuments).not.toHaveBeenCalled();
    expect(el.textContent).toContain(
      'Give the search something to look for',
    );
  });

  it('a 400 from the API shows a readable error', async () => {
    const { fixture, el } = await create();
    api.searchDocuments.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 400 })),
    );
    setInput(el, '#search-q', 'test');
    submit(fixture, el);

    expect(el.textContent).toContain('Check the search');
  });

  it('clear empties the form and the results', async () => {
    const { fixture, el } = await create();
    setInput(el, '#search-q', 'flat white');
    submit(fixture, el);
    expect(el.textContent).toContain('receipt.png');

    (el.querySelector('.form-actions .btn:not(.btn-primary)') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect((el.querySelector('#search-q') as HTMLInputElement).value).toBe('');
    expect(el.textContent).not.toContain('receipt.png');
    expect(el.textContent).not.toContain('document found');
  });

  it('the category filter is sent to the API', async () => {
    const { fixture, el } = await create();
    const select = el.querySelector('#search-category') as HTMLSelectElement;
    select.value = 'Invoice';
    select.dispatchEvent(new Event('change'));
    submit(fixture, el);

    expect(api.searchDocuments).toHaveBeenCalledWith(
      'ws-1',
      expect.objectContaining({ category: 'Invoice' }),
    );
  });
});
