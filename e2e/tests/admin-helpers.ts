import { expect, type Page } from '@playwright/test';

// Shared E2E sign-in helpers (used by backup.spec.ts and monitoring.spec.ts).
// The seeded admin (admin@docuarchive.local) is created by e2e/start-app.sh
// with the test-only ADMIN_PASSWORD; the first sign-in forces a password
// change. Runs against the local app only — never the live site.
const PASSWORD = 'Password123!@#';
const SEED_ADMIN_EMAIL = 'admin@docuarchive.local';
const SEED_ADMIN_PASSWORD = 'E2EAdminPassword123!@#';
const ADMIN_PASSWORD = 'AdminChanged123!@#';

export async function registerAndLogin(
  page: Page,
  emailPrefix: string,
  displayName: string,
) {
  const email = `e2e-${emailPrefix}-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`;
  await page.goto('/register');
  await page.getByLabel('Display name').fill(displayName);
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

export async function loginAsAdmin(page: Page) {
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
