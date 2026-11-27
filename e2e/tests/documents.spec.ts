import path from 'node:path';
import { test, expect, type Page } from '@playwright/test';

// Upload → OCR → Ready → search, through the real UI against the local app.
// The demo receipt is fictional (THE CORNER CAFE) — the same fixture the
// backend tests use. Local OCR (tesseract) is fast; the wait is generous
// because the first run also warms up the worker.
const PASSWORD = 'Password123!@#';
const RECEIPT = path.join(process.cwd(), 'fixtures/demo-receipt.png');

async function registerAndLogin(page: Page) {
  const email = `e2e-docs-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`;
  await page.goto('/register');
  await page.getByLabel('Display name').fill('E2E Docs User');
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

async function createWorkspace(page: Page, name: string) {
  await page.goto('/workspaces');
  await page.getByLabel('Name').fill(name);
  await page.getByRole('button', { name: 'Create workspace' }).click();
  await expect(page.getByText('Workspace created.')).toBeVisible();
}

test.describe('documents', () => {
  test('upload a receipt, it reaches Ready, search finds it', async ({ page }) => {
    await registerAndLogin(page);
    await createWorkspace(page, 'E2E Archive');

    await page.goto('/documents');
    const workspaceSelect = page.getByLabel('Select workspace');
    if (await workspaceSelect.isVisible()) {
      await workspaceSelect.selectOption({ label: 'E2E Archive' });
    }
    await expect(page.getByText('Documents in E2E Archive')).toBeVisible();

    const uploadInput = page.locator('#document-upload');
    await expect(uploadInput).toBeEnabled();
    await uploadInput.setInputFiles(RECEIPT);
    await expect(page.getByText('Document uploaded.')).toBeVisible();

    const row = page.locator('tr', { hasText: 'demo-receipt.png' });
    await expect(row).toBeVisible();
    // OCR runs in the background; the list refreshes until the document is Ready.
    await expect(row.getByText('Ready')).toBeVisible({ timeout: 120_000 });

    // Search finds it by OCR text, and an unrelated term finds nothing.
    const search = page.getByPlaceholder('Search file name, vendor or document text…');
    await search.fill('flat white');
    await expect(row).toBeVisible({ timeout: 15_000 });
    await search.fill('plumber');
    await expect(row).toBeHidden({ timeout: 15_000 });
  });
});
