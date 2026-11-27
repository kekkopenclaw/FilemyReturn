import { promises as fs } from 'node:fs';
import path from 'node:path';
import { test, expect, type Page } from '@playwright/test';

// Phase 8 backup: a system administrator downloads the whole system as one
// ZIP; anyone else is told the page is for system administrators only.
// The admin flow signs in as the seeded admin (admin@docuarchive.local,
// password from e2e/start-app.sh), changes the forced first-login password,
// then downloads. Runs against the local app only — never the live site.
const PASSWORD = 'Password123!@#';
const SEED_ADMIN_EMAIL = 'admin@docuarchive.local';
const SEED_ADMIN_PASSWORD = 'E2EAdminPassword123!@#';
const ADMIN_PASSWORD = 'AdminChanged123!@#';

async function registerAndLogin(page: Page) {
  const email = `e2e-backup-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`;
  await page.goto('/register');
  await page.getByLabel('Display name').fill('E2E Backup User');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
  await page.getByLabel('Confirm password').fill(PASSWORD);
  await page.getByRole('button', { name: 'Create account' }).click();
  await expect(page).toHaveURL(/\/login$/);

  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL(/\/dashboard$/);
}

async function loginAsAdmin(page: Page) {
  await page.goto('/login');
  await page.getByLabel('Email').fill(SEED_ADMIN_EMAIL);
  await page.getByLabel('Password', { exact: true }).fill(SEED_ADMIN_PASSWORD);
  await page.getByRole('button', { name: 'Sign in' }).click();
  // Two cases, because every test in this file shares the one seeded admin:
  // the first sign-in forces a password change; a later sign-in is rejected
  // (the password was already changed) and must use the new password.
  const outcome = await Promise.race([
    page
      .getByRole('heading', { name: 'Change your password' })
      .waitFor()
      .then(() => 'change' as const),
    page
      .getByRole('alert')
      .waitFor()
      .then(() => 'wrong-password' as const),
  ]);
  if (outcome === 'change') {
    await expect(page.getByText('You must change your password')).toBeVisible();
    await page.getByLabel('Current password').fill(SEED_ADMIN_PASSWORD);
    await page.getByLabel('New password', { exact: true }).fill(ADMIN_PASSWORD);
    await page.getByLabel('Confirm new password').fill(ADMIN_PASSWORD);
    await page.getByRole('button', { name: 'Change password' }).click();
    await expect(
      page.getByText('Your password has been changed'),
    ).toBeVisible();
    await page.getByRole('link', { name: 'Sign in' }).click();
    await expect(page).toHaveURL(/\/login$/);

    await page.getByLabel('Email').fill(SEED_ADMIN_EMAIL);
    await page.getByLabel('Password', { exact: true }).fill(ADMIN_PASSWORD);
    await page.getByRole('button', { name: 'Sign in' }).click();
  } else {
    await page.getByLabel('Email').fill(SEED_ADMIN_EMAIL);
    await page.getByLabel('Password', { exact: true }).fill(ADMIN_PASSWORD);
    await page.getByRole('button', { name: 'Sign in' }).click();
  }
  await expect(page).toHaveURL(/\/dashboard$/);
}

test.describe('backup', () => {
  test('non-admins are told the page is for system administrators', async ({
    page,
  }) => {
    await registerAndLogin(page);
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
