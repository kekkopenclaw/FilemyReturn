import path from 'node:path';
import { test, expect, type Page } from '@playwright/test';

// Phase 4 organisation: metadata editing. Open a document's details, edit
// its file name, vendor, total and category, save, and check the changes
// stick — the row shows the new file name, and reopening the form shows the
// saved vendor, total and category. No OCR is needed: editing works on any
// document, even before it has been read (tesseract is not in this VM).
const PASSWORD = 'Password123!@#';
const RECEIPT = path.join(process.cwd(), 'fixtures/demo-receipt.png');

async function registerAndLogin(page: Page) {
  const email = `e2e-meta-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`;
  await page.goto('/register');
  await page.getByLabel('Display name').fill('E2E Metadata User');
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

test.describe('metadata editing', () => {
  test('edit file name, vendor, total and category, and the changes stick', async ({
    page,
  }) => {
    await registerAndLogin(page);
    await createWorkspace(page, 'E2E Metadata');

    await page.goto('/documents');
    const workspaceSelect = page.getByLabel('Select workspace');
    if (await workspaceSelect.isVisible()) {
      await workspaceSelect.selectOption({ label: 'E2E Metadata' });
    }
    await expect(page.getByText('Documents in E2E Metadata')).toBeVisible();

    const uploadInput = page.locator('#document-upload');
    await expect(uploadInput).toBeEnabled();
    await uploadInput.setInputFiles(RECEIPT);
    await expect(page.getByText('Document uploaded.')).toBeVisible();
    const row = page.locator('tr', { hasText: 'demo-receipt.png' });
    await expect(row).toBeVisible();
    // Wait for a terminal state (Ready, or Failed when the VM has no
    // tesseract) so the list stops polling every 5s — polling replaces
    // the row and detaches buttons mid-click.
    await expect(row.getByText(/^(Ready|Failed)$/)).toBeVisible({
      timeout: 120_000,
    });

    await row.getByRole('button', { name: 'Details' }).click();
    await page.getByRole('button', { name: 'Edit details' }).click();
    await page.getByLabel('File name').fill('weekly-shop.png');
    await page.getByLabel('Vendor').fill("Sainsbury's");
    await page.getByLabel('Total').fill('12.34');
    await page.getByLabel('Category').selectOption('Invoice');
    await page.getByRole('button', { name: 'Save details' }).click();

    // The row shows the new file name and the form closed.
    await expect(
      page.locator('tr', { hasText: 'weekly-shop.png' }),
    ).toBeVisible();
    await expect(
      page.getByRole('button', { name: 'Edit details' }),
    ).toBeVisible();

    // Reopening the form shows the saved values.
    await page.getByRole('button', { name: 'Edit details' }).click();
    await expect(page.getByLabel('Vendor')).toHaveValue("Sainsbury's");
    await expect(page.getByLabel('Total')).toHaveValue('12.34');
    await expect(page.getByLabel('Category')).toHaveValue('Invoice');
  });

  test('on a phone the edit form fits with no horizontal overflow', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await registerAndLogin(page);
    await createWorkspace(page, 'E2E Metadata Phone');

    await page.goto('/documents');
    const workspaceSelect = page.getByLabel('Select workspace');
    if (await workspaceSelect.isVisible()) {
      await workspaceSelect.selectOption({ label: 'E2E Metadata Phone' });
    }
    await expect(page.getByText('Documents in E2E Metadata Phone')).toBeVisible();

    const uploadInput = page.locator('#document-upload');
    await uploadInput.setInputFiles(RECEIPT);
    await expect(page.getByText('Document uploaded.')).toBeVisible();
    const row = page.locator('tr', { hasText: 'demo-receipt.png' });
    await expect(row).toBeVisible();
    await expect(row.getByText(/^(Ready|Failed)$/)).toBeVisible({
      timeout: 120_000,
    });

    await row.getByRole('button', { name: 'Details' }).click();
    await page.getByRole('button', { name: 'Edit details' }).click();
    await expect(page.locator('.metadata-form')).toBeVisible();

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBe(0);
  });
});
