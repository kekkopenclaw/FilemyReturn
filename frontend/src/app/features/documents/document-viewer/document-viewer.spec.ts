import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { Observable, of, throwError } from 'rxjs';
import { ApiService } from '../../../core/api.service';
import { DocumentSummary } from '../../../core/models';
import { DocumentViewer } from './document-viewer';

// Phase 6 viewer: PDFs render in an iframe, images in an <img>, TIFF/BMP
// (backend 400) get a plain message plus Download, other failures get a
// retry-or-download message. The preview is fetched through ApiService so
// the Bearer token travels with the request.
describe('DocumentViewer', () => {
  const pdfDoc: DocumentSummary = {
    id: 'doc-pdf',
    originalFileName: 'test.pdf',
    mimeType: 'application/pdf',
    fileSizeBytes: 300,
    status: 'Ready',
    ocrStatus: 'Done',
    archiveId: null,
    createdAt: '2026-11-27T00:00:00Z',
  };

  const pngDoc: DocumentSummary = {
    ...pdfDoc,
    id: 'doc-png',
    originalFileName: 'scan.png',
    mimeType: 'image/png',
  };

  // jsdom has no URL.createObjectURL — the viewer creates one blob URL
  // per preview (and one per download), so stub both halves here.
  beforeEach(() => {
    Object.defineProperty(URL, 'createObjectURL', {
      value: vi.fn(() => 'blob:fake-preview'),
      configurable: true,
    });
    Object.defineProperty(URL, 'revokeObjectURL', {
      value: vi.fn(),
      configurable: true,
    });
  });

  const create = async (
    doc: DocumentSummary,
    preview: Observable<Blob>,
  ): Promise<{ closed: () => number; html: () => string }> => {
    const api = { previewDocument: vi.fn(() => preview) };
    await TestBed.configureTestingModule({
      imports: [DocumentViewer],
      providers: [{ provide: ApiService, useValue: api }],
    }).compileComponents();
    const fixture = TestBed.createComponent(DocumentViewer);
    fixture.componentRef.setInput('workspaceId', 'ws-1');
    fixture.componentRef.setInput('doc', doc);

    let closedCount = 0;
    const viewer = fixture.componentInstance as DocumentViewer;
    viewer.closed.subscribe(() => closedCount++);

    fixture.detectChanges();
    return {
      closed: () => closedCount,
      html: () => fixture.nativeElement.innerHTML as string,
    };
  };

  it('renders a PDF in an iframe once the preview loads', () => {
    return create(pdfDoc, of(new Blob(['%PDF'], { type: 'application/pdf' }))).then(
      ({ html }) => {
        // of() resolves synchronously in ngOnInit, so the iframe is
        // already there and the loading state is gone.
        expect(html()).toContain('<iframe');
        expect(html()).toContain('Preview of test.pdf');
        expect(html()).not.toContain('Loading preview…');
      },
    );
  });

  it('renders an image with an <img>', () => {
    return create(pngDoc, of(new Blob(['png'], { type: 'image/png' }))).then(
      ({ html }) => {
        expect(html()).toContain('<img');
        expect(html()).toContain('Preview of scan.png');
        expect(html()).not.toContain('<iframe');
      },
    );
  });

  it('a 400 (TIFF/BMP) explains the type cannot preview and offers Download', () => {
    const tiffDoc = { ...pngDoc, mimeType: 'image/tiff' };
    const err = throwError(
      () => new HttpErrorResponse({ status: 400, statusText: 'Bad Request' }),
    );
    return create(tiffDoc, err).then(({ html }) => {
      expect(html()).toContain('can’t be previewed in the browser');
      expect(html()).toContain('Download');
      expect(html()).not.toContain('<iframe');
      expect(html()).not.toContain('<img');
    });
  });

  it('any other failure shows an error and a Download instead button', () => {
    const err = throwError(
      () =>
        new HttpErrorResponse({ status: 500, statusText: 'Server Error' }),
    );
    return create(pdfDoc, err).then(({ html }) => {
      expect(html()).toContain('Could not load the preview');
      expect(html()).toContain('Download instead');
    });
  });

  it('Close emits the closed event', () => {
    return create(pdfDoc, of(new Blob(['%PDF']))).then(({ closed }) => {
      const button = document.querySelector(
        '.viewer-close',
      ) as HTMLButtonElement;
      button.click();
      expect(closed()).toBe(1);
    });
  });
});
