import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { of, throwError } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { MonitoringStatus } from '../../core/models';
import { Monitoring, formatUptime } from './monitoring';

// Phase 9 monitoring: a system administrator sees a read-only health
// snapshot — uptime, version, counts by status, review-queue depth, storage,
// failed logins, last backup. Non-admins are told the page is for system
// administrators, and a Refresh button reloads the snapshot.
describe('formatUptime', () => {
  it('formats seconds, minutes, hours and days', () => {
    expect(formatUptime(0)).toBe('0s');
    expect(formatUptime(45)).toBe('45s');
    expect(formatUptime(90)).toBe('1m 30s');
    expect(formatUptime(2 * 3600 + 14 * 60)).toBe('2h 14m');
    expect(formatUptime(3 * 86400 + 2 * 3600)).toBe('3d 2h');
  });
});

describe('Monitoring', () => {
  const status: MonitoringStatus = {
    version: '1.0.0',
    commit: 'abc123def',
    uptimeSeconds: 2 * 3600 + 14 * 60,
    databaseProvider: 'Sqlite',
    databaseSizeBytes: 2048,
    userCount: 4,
    workspaceCount: 2,
    documentCount: 12,
    documentsByStatus: {
      total: 12,
      uploaded: 2,
      processing: 1,
      ready: 8,
      failed: 1,
      ocrQueued: 1,
      ocrProcessing: 1,
      ocrFailed: 0,
      needsReview: 3,
    },
    fileCount: 12,
    storageSizeBytes: 1536,
    failedLoginsLast24h: 3,
    lastBackupAtUtc: '2026-11-27T08:00:00Z',
    lastBackupDetails:
      '12 documents, 12 files, 3584 bytes in docuarchive-backup-20261127-080000.zip',
    generatedAtUtc: '2026-11-27T08:16:00Z',
  };

  const create = async (apiOverrides: Partial<Record<string, unknown>> = {}) => {
    const api: { getMonitoring: ReturnType<typeof vi.fn> } = {
      getMonitoring: vi.fn(() => of(status)),
      ...apiOverrides,
    };
    await TestBed.configureTestingModule({
      imports: [Monitoring],
      providers: [{ provide: ApiService, useValue: api }],
    }).compileComponents();
    const fixture = TestBed.createComponent(Monitoring);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    return { fixture, el, api };
  };

  it('shows the snapshot: uptime, version, counts and storage', async () => {
    const { el } = await create();
    expect(el.textContent).toContain('2h 14m');
    expect(el.textContent).toContain('1.0.0');
    expect(el.textContent).toContain('abc123def');
    expect(el.textContent).toContain('Sqlite');
    expect(el.textContent).toContain('2.0 KB');
    expect(el.textContent).toContain('Needs review');
    expect(el.textContent).toContain('Refresh');
  });

  it('says so when no backup has ever been downloaded', async () => {
    const { el } = await create({
      getMonitoring: vi.fn(() =>
        of({ ...status, lastBackupAtUtc: null, lastBackupDetails: null }),
      ),
    });
    expect(el.textContent).toContain('Never downloaded');
  });

  it('tells non-admins the page is for system administrators', async () => {
    const { el } = await create({
      getMonitoring: vi.fn(() =>
        throwError(
          () => new HttpErrorResponse({ status: 403, statusText: 'Forbidden' }),
        ),
      ),
    });
    expect(el.textContent).toContain('System administrators only');
    expect(el.textContent).not.toContain('Refresh');
  });

  it('shows an error with a retry button when loading fails', async () => {
    const getMonitoring: ReturnType<typeof vi.fn> = vi.fn(() =>
      throwError(() => new Error('network down')),
    );
    const { fixture, el, api } = await create({ getMonitoring });
    expect(el.textContent).toContain('Could not load the monitoring snapshot');
    getMonitoring.mockReturnValue(of(status));
    const button = Array.from(el.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Try again'),
    ) as HTMLButtonElement;
    button.click();
    fixture.detectChanges();
    expect(api.getMonitoring).toHaveBeenCalledTimes(2);
    expect(el.textContent).toContain('Refresh');
  });

  it('refreshes the snapshot on demand', async () => {
    const { fixture, el, api } = await create();
    const button = Array.from(el.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Refresh'),
    ) as HTMLButtonElement;
    button.click();
    fixture.detectChanges();
    expect(api.getMonitoring).toHaveBeenCalledTimes(2);
  });
});
