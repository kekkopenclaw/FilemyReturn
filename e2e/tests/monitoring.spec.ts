import { test, expect } from '@playwright/test';

// Phase 9 monitoring: a system administrator sees a read-only health
// snapshot — uptime, version, counts, review-queue depth, failed logins,
// last backup — with a manual Refresh button. Anyone else is told the page
// is for system administrators only. Runs against the local app only —
// never the live site. Admin sign-in follows the same pattern as
// backup.spec.ts (seeded admin, forced first-login password change).
import { loginAsAdmin, registerAndLogin } from './admin-helpers';

test.describe('monitoring', () => {
  test('non-admins are told the page is for system administrators', async ({
    page,
  }) => {
    await registerAndLogin(page, 'monitoring', 'E2E Monitoring User');
    await page.goto('/monitoring');
    await expect(
      page.getByText('System administrators only'),
    ).toBeVisible();
    await expect(
      page.getByRole('button', { name: 'Refresh' }),
    ).toHaveCount(0);
  });

  test('an admin sees the snapshot and can refresh it', async ({ page }) => {
    await loginAsAdmin(page);
    await page.goto('/monitoring');
    await expect(page.getByText('Documents by status')).toBeVisible();
    await expect(page.getByText('Sqlite')).toBeVisible();
    await expect(page.getByText('Needs review', { exact: true })).toBeVisible();
    await expect(page.getByText('Failed logins (24h)')).toBeVisible();
    // The snapshot is live data: refreshing re-reads it without an error.
    await page.getByRole('button', { name: 'Refresh' }).click();
    await expect(page.getByText('Documents by status')).toBeVisible();
  });

  test('the monitoring page fits a phone screen', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await loginAsAdmin(page);
    await page.goto('/monitoring');
    await expect(page.getByText('Documents by status')).toBeVisible();
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBe(0);
  });
});
