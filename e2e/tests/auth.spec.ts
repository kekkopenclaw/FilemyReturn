import { test, expect } from '@playwright/test';

// Auth flows through the real UI: register a fresh user, sign in with it,
// and a wrong password must show an error instead of signing in.
function uniqueEmail(prefix: string) {
  return `${prefix}-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`;
}

test.describe('auth', () => {
  test('register then login reaches the dashboard', async ({ page }) => {
    const email = uniqueEmail('e2e-auth');
    const password = 'Password123!@#';

    await page.goto('/register');
    await page.getByLabel('Display name').fill('E2E User');
    await page.getByLabel('Email').fill(email);
    await page.getByLabel('Password', { exact: true }).fill(password);
    await page.getByLabel('Confirm password').fill(password);
    await page.getByRole('button', { name: /Create account|Register|Sign up/i }).click();

    // Registration sends the user to the login page.
    await expect(page).toHaveURL(/\/login$/);

    await page.getByLabel('Email').fill(email);
    await page.getByLabel('Password', { exact: true }).fill(password);
    await page.getByRole('button', { name: /Sign in|Log in/i }).click();

    await expect(page).toHaveURL(/\/dashboard$/);
    await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();
  });

  test('wrong password shows an error and stays on login', async ({ page }) => {
    const email = uniqueEmail('e2e-wrongpw');

    await page.goto('/register');
    await page.getByLabel('Display name').fill('E2E User');
    await page.getByLabel('Email').fill(email);
    await page.getByLabel('Password', { exact: true }).fill('Password123!@#');
    await page.getByLabel('Confirm password').fill('Password123!@#');
    await page.getByRole('button', { name: /Create account|Register|Sign up/i }).click();
    await expect(page).toHaveURL(/\/login$/);

    await page.getByLabel('Email').fill(email);
    await page.getByLabel('Password', { exact: true }).fill('WrongPassword123!@#');
    await page.getByRole('button', { name: /Sign in|Log in/i }).click();

    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page).toHaveURL(/\/login$/);
  });
});
