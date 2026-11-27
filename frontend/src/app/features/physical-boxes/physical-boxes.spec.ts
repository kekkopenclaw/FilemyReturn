import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { describe, expect, it, vi } from 'vitest';
import { of, throwError } from 'rxjs';
import { ApiService } from '../../core/api.service';
import {
  DocumentSummary,
  PhysicalBox,
  Workspace,
} from '../../core/models';
import { WorkspaceService } from '../../core/workspace.service';
import {
  buildBoxLabelHtml,
  PhysicalBoxes,
} from './physical-boxes';

// Phase 7 physical archive: boxes are the real-world home of paper originals
// — a name, a physical location, a printed label code and a QR code. The
// form must refuse a box with no name or no location, and create / edit /
// delete / view-contents all go through the API.
describe('PhysicalBoxes', () => {
  const workspace: Workspace = {
    id: 'ws-1',
    name: 'Test WS',
    description: null,
    role: 'Owner',
    createdAt: '2026-11-27T00:00:00Z',
    createdById: 'u1',
  };

  const box: PhysicalBox = {
    id: 'b1',
    workspaceId: 'ws-1',
    name: 'Tax archive 2025',
    code: 'BOX-7K2P9Q',
    location: 'Shelf B, row 3',
    description: 'Receipts and invoices',
    documentCount: 2,
    createdAt: '2026-11-27T00:00:00Z',
  };

  const docs: DocumentSummary[] = [
    {
      id: 'd1',
      originalFileName: 'tesco-receipt.pdf',
      mimeType: 'application/pdf',
      fileSizeBytes: 1234,
      status: 'Ready',
      ocrStatus: 'Completed',
      archiveId: null,
      createdAt: '2026-11-27T00:00:00Z',
    },
  ];

  const qrSvg = '<svg xmlns="http://www.w3.org/2000/svg"><rect width="8" height="8"/></svg>';

  let api: {
    getPhysicalBoxes: ReturnType<typeof vi.fn>;
    getPhysicalBoxDocuments: ReturnType<typeof vi.fn>;
    getPhysicalBoxQr: ReturnType<typeof vi.fn>;
    createPhysicalBox: ReturnType<typeof vi.fn>;
    updatePhysicalBox: ReturnType<typeof vi.fn>;
    deletePhysicalBox: ReturnType<typeof vi.fn>;
  };

  const create = async (boxes: PhysicalBox[] = [box], boxParam: string | null = null) => {
    api = {
      getPhysicalBoxes: vi.fn(() => of(boxes)),
      getPhysicalBoxDocuments: vi.fn(() => of(docs)),
      getPhysicalBoxQr: vi.fn(() => of(qrSvg)),
      createPhysicalBox: vi.fn(
        (_: string, input: Record<string, unknown>) =>
          of({ ...box, id: 'b-new', code: 'BOX-NEW123', ...input }),
      ),
      updatePhysicalBox: vi.fn(
        (_: string, __: string, input: Record<string, unknown>) =>
          of({ ...box, ...input }),
      ),
      deletePhysicalBox: vi.fn(() => of(undefined)),
    };
    const workspaceService = {
      workspaces: signal([workspace]),
      loading: signal(false),
      selectedWorkspaceId: signal('ws-1'),
      selectedWorkspace: signal(workspace),
    };
    const route = {
      snapshot: { queryParamMap: { get: (_: string) => boxParam } },
    };
    await TestBed.configureTestingModule({
      imports: [PhysicalBoxes],
      providers: [
        { provide: ApiService, useValue: api },
        { provide: WorkspaceService, useValue: workspaceService },
        { provide: ActivatedRoute, useValue: route },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(PhysicalBoxes);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    return { fixture, el };
  };

  const setInput = (el: HTMLElement, selector: string, value: string) => {
    const input = el.querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };

  const submitForm = (el: HTMLElement) => {
    const form = el.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
  };

  const clickButton = (el: HTMLElement, text: string) => {
    const button = Array.from(el.querySelectorAll('button')).find((b) =>
      b.textContent?.includes(text),
    ) as HTMLButtonElement;
    button.click();
  };

  it('renders a box with its code, location and document count', async () => {
    const { el } = await create();
    expect(el.textContent).toContain('Tax archive 2025');
    expect(el.textContent).toContain('BOX-7K2P9Q');
    expect(el.textContent).toContain('Shelf B, row 3');
    expect(el.textContent).toContain('2 documents');
  });

  it('refuses a box with no name', async () => {
    const { fixture, el } = await create([]);
    setInput(el, '#box-location', 'Shelf B');
    submitForm(el);
    fixture.detectChanges();
    expect(api.createPhysicalBox).not.toHaveBeenCalled();
    expect(el.textContent).toContain('a name is required');
  });

  it('refuses a box with no location', async () => {
    const { fixture, el } = await create([]);
    setInput(el, '#box-name', 'Loft boxes');
    submitForm(el);
    fixture.detectChanges();
    expect(api.createPhysicalBox).not.toHaveBeenCalled();
    expect(el.textContent).toContain('a location is required');
  });

  it('creating a box calls the API and shows its label code', async () => {
    const { fixture, el } = await create([]);
    setInput(el, '#box-name', 'Loft boxes');
    setInput(el, '#box-location', 'Loft, north wall');
    submitForm(el);
    fixture.detectChanges();
    expect(api.createPhysicalBox).toHaveBeenCalledWith('ws-1', {
      name: 'Loft boxes',
      location: 'Loft, north wall',
      description: null,
    });
    expect(el.textContent).toContain('Box created');
    expect(el.textContent).toContain('BOX-NEW123');
  });

  it('a duplicate box name shows an error and adds nothing', async () => {
    const { fixture, el } = await create([]);
    api.createPhysicalBox.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({ status: 400, statusText: 'Bad Request' }),
      ),
    );
    setInput(el, '#box-name', 'Tax archive 2025');
    setInput(el, '#box-location', 'Shelf B');
    submitForm(el);
    fixture.detectChanges();
    expect(el.textContent).toContain('unique');
    expect(el.querySelectorAll('.box-row').length).toBe(0);
  });

  it('editing loads the box into the form and saving calls update', async () => {
    const { fixture, el } = await create();
    clickButton(el, 'Edit box Tax archive 2025');
    fixture.detectChanges();
    expect((el.querySelector('#box-name') as HTMLInputElement).value).toBe(
      'Tax archive 2025',
    );
    expect((el.querySelector('#box-location') as HTMLInputElement).value).toBe(
      'Shelf B, row 3',
    );
    setInput(el, '#box-location', 'Cupboard, top shelf');
    submitForm(el);
    fixture.detectChanges();
    expect(api.updatePhysicalBox).toHaveBeenCalledWith('ws-1', 'b1', {
      name: 'Tax archive 2025',
      location: 'Cupboard, top shelf',
      description: 'Receipts and invoices',
    });
    expect(el.textContent).toContain('Box updated.');
  });

  it('deleting a box calls the API and removes it from the list', async () => {
    const { fixture, el } = await create();
    clickButton(el, 'Delete box Tax archive 2025');
    fixture.detectChanges();
    expect(api.deletePhysicalBox).toHaveBeenCalledWith('ws-1', 'b1');
    expect(el.querySelectorAll('.box-row').length).toBe(0);
    expect(el.textContent).toContain('Its documents were kept');
  });

  it('viewing a box shows its documents and QR code', async () => {
    const { fixture, el } = await create();
    clickButton(el, 'View contents');
    fixture.detectChanges();
    expect(api.getPhysicalBoxDocuments).toHaveBeenCalledWith('ws-1', 'b1');
    expect(api.getPhysicalBoxQr).toHaveBeenCalledWith('ws-1', 'b1');
    expect(el.textContent).toContain('tesco-receipt.pdf');
    expect(el.querySelector('.qr-image')?.innerHTML).toContain('<svg');
    expect(el.textContent).toContain('Print label');
  });

  it('the QR deep link selects the box on load', async () => {
    const { el } = await create([box], 'BOX-7K2P9Q');
    expect(api.getPhysicalBoxDocuments).toHaveBeenCalledWith('ws-1', 'b1');
    expect(el.textContent).toContain('tesco-receipt.pdf');
  });

  it('buildBoxLabelHtml includes the box and escapes markup', () => {
    const evil: PhysicalBox = {
      ...box,
      name: 'Box <script>alert(1)</script>',
      location: 'Shelf & "row" 3',
    };
    const html = buildBoxLabelHtml(evil, qrSvg);
    expect(html).toContain('BOX-7K2P9Q');
    expect(html).toContain('Shelf &amp; &quot;row&quot; 3');
    expect(html).toContain('<svg');
    expect(html).not.toContain('<script>alert(1)</script>');
  });
});
