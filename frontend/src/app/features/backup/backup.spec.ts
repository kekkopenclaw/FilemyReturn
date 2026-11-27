import { HttpErrorResponse, HttpHeaders, HttpResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { of, throwError } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { BackupInfo } from '../../core/models';
import {
  Backup,
  fileNameFromContentDisposition,
  formatBytes,
} from './backup';

// Phase 8 backup: a system administrator downloads the whole system as one
// ZIP. The page shows what the backup contains, refuses non-admins with a
// readable message, and downloads through the browser's save dialog.
describe('formatBytes', () => {
  it('formats bytes, kilobytes and megabytes', () => {
    expect(formatBytes(0)).toBe('0 B');
    expect(formatBytes(512)).toBe('512 B');
    expect(formatBytes(1536)).toBe('1.5 KB');
    expect(formatBytes(2 * 1024 * 1024)).toBe('2.0 MB');
  });
});

describe('fileNameFromContentDisposition', () => {
  it('reads a quoted filename', () => {
    expect(
      fileNameFromContentDisposition(
        'attachment; filename="docuarchive-backup-20261127-073000.zip"',
      ),
    ).toBe('docuarchive-backup-20261127-073000.zip');
  });

  it('decodes an RFC 5987 filename', () => {
    expect(
      fileNameFromContentDisposition(
        "attachment; filename*=UTF-8''docuarchive-backup-20261127-073000.zip",
      ),
    ).toBe('docuarchive-backup-20261127-073000.zip');
  });

  it('returns null when there is no filename', () => {
    expect(fileNameFromContentDisposition(null)).toBeNull();
    expect(fileNameFromContentDisposition('attachment')).toBeNull();
  });
});

describe('Backup', () => {
  const info: BackupInfo = {
    provider: 'Sqlite',
    databaseSizeBytes: 2048,
    documentCount: 12,
    workspaceCount: 3,
    fileCount: 12,
    storageSizeBytes: 1536,
    generatedAtUtc: '2026-11-27T07:30:00Z',
  };

  const create = async (apiOverrides: Partial<Record<string, unknown>> = {}) => {
    const api: {
      getBackupInfo: ReturnType<typeof vi.fn>;
      downloadBackup: ReturnType<typeof vi.fn>;
    } = {
      getBackupInfo: vi.fn(() => of(info)),
      downloadBackup: vi.fn(() =>
        of(
          new HttpResponse({
            body: new Blob(['PK'], { type: 'application/zip' }),
            headers: new HttpHeaders({
              'Content-Disposition':
                'attachment; filename="docuarchive-backup-20261127-073000.zip"',
            }),
          }),
        ),
      ),
      ...apiOverrides,
    };
    await TestBed.configureTestingModule({
      imports: [Backup],
      providers: [{ provide: ApiService, useValue: api }],
    }).compileComponents();
    const fixture = TestBed.createComponent(Backup);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    return { fixture, el, api };
  };

  const clickButton = (el: HTMLElement, text: string) => {
    const button = Array.from(el.querySelectorAll('button')).find((b) =>
      b.textContent?.includes(text),
    ) as HTMLButtonElement;
    button.click();
  };

  it('shows what the backup contains', async () => {
    const { el } = await create();
    expect(el.textContent).toContain('Sqlite');
    expect(el.textContent).toContain('2.0 KB');
    expect(el.textContent).toContain('12');
    expect(el.textContent).toContain('3');
    expect(el.textContent).toContain('1.5 KB');
    expect(el.textContent).toContain('Download backup');
  });

  it('tells non-admins the page is for system administrators', async () => {
    const { el } = await create({
      getBackupInfo: vi.fn(() =>
        throwError(
          () => new HttpErrorResponse({ status: 403, statusText: 'Forbidden' }),
        ),
      ),
    });
    expect(el.textContent).toContain('System administrators only');
    expect(el.textContent).not.toContain('Download backup');
  });

  it('shows an error with a retry button when loading fails', async () => {
    const getBackupInfo: ReturnType<typeof vi.fn> = vi.fn(() =>
      throwError(() => new Error('network down')),
    );
    const { fixture, el, api } = await create({ getBackupInfo });
    expect(el.textContent).toContain('Could not load the backup details');
    getBackupInfo.mockReturnValue(of(info));
    clickButton(el, 'Try again');
    fixture.detectChanges();
    expect(api.getBackupInfo).toHaveBeenCalledTimes(2);
    expect(el.textContent).toContain('Download backup');
  });

  it('downloads the ZIP and shows a confirmation', async () => {
    const createObjectURL = vi
      .spyOn(URL, 'createObjectURL')
      .mockReturnValue('blob:fake');
    const revokeObjectURL = vi
      .spyOn(URL, 'revokeObjectURL')
      .mockImplementation(() => {});
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click');
    const { fixture, el, api } = await create();
    clickButton(el, 'Download backup');
    fixture.detectChanges();
    expect(api.downloadBackup).toHaveBeenCalledTimes(1);
    expect(createObjectURL).toHaveBeenCalledTimes(1);
    expect(click).toHaveBeenCalledTimes(1);
    expect(el.textContent).toContain('Backup downloaded');
    createObjectURL.mockRestore();
    revokeObjectURL.mockRestore();
    click.mockRestore();
  });

  it('shows the server message when the download is refused', async () => {
    const detail = new Blob(
      [JSON.stringify({ detail: 'PostgreSQL backups use pg_dump.' })],
      { type: 'application/problem+json' },
    );
    const { fixture, el } = await create({
      downloadBackup: vi.fn(() =>
        throwError(
          () =>
            new HttpErrorResponse({ status: 501, statusText: 'x', error: detail }),
        ),
      ),
    });
    clickButton(el, 'Download backup');
    // The error body is a Blob, so the message is read back asynchronously.
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.textContent).toContain('PostgreSQL backups use pg_dump.');
  });
});
