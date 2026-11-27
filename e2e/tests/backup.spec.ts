import { promises as fs } from 'node:fs';
import path from 'node:path';
import { test, expect } from '@playwright/test';

// Phase 8 backup: a system administrator downloads the whole system as one
// ZIP; anyone else is told the page is for system administrators only.
// The admin flow signs in as the seeded admin (admin@docuarchive.local,
// password from e2e/start-app.sh), changes the forced first-login password,
// then downloads. Runs against the local app only — never the live site.
import { loginAsAdmin, registerAndLogin } from './admin-helpers';

test.describe('backup', () => {
  test('non-admins are told the page is for system administrators', async ({
    page,
  }) => {
    await registerAndLogin(page, 'backup', 'E2E Backup User');
    await page.goto('/backup');
    await expect(
      page.getByText('System administrators only'),
    ).toBeVisible();
    await expect(
      page.getByRole('button', { name: 'Download backup' }),
    ).toHaveCount(0);
  });

  test('an admin downloads a real ZIP', async ({ page }, testInfo) => {
    await loginAsAdmin(page);
    await page.goto('/backup');
    await expect(page.getByText('What the backup contains')).toBeVisible();
    await expect(page.getByText('Sqlite')).toBeVisible();

    const downloadPromise = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Download backup' }).click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toMatch(
      /^docuarchive-backup-\d{8}-\d{6}\.zip$/,
    );
    const savePath = path.join(
      testInfo.outputDir,
      download.suggestedFilename(),
    );
    await download.saveAs(savePath);
    const bytes = await fs.readFile(savePath);
    expect(bytes.subarray(0, 2).toString('latin1')).toBe('PK'); // ZIP magic
    expect(bytes.length).toBeGreaterThan(100);
    await expect(page.getByText('Backup downloaded')).toBeVisible();
  });

  test('the backup page fits a phone screen', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await loginAsAdmin(page);
    await page.goto('/backup');
    await expect(page.getByText('What the backup contains')).toBeVisible();
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBe(0);
  });
});
