import path from 'node:path';
import { test, expect, type Page } from '@playwright/test';

// Phase 4 organisation: create a tag, assign it to an uploaded document,
// filter the list by it, unassign it, delete it — through the real UI
// against the local app. Tagging does not need OCR, so the document only
// has to be visible in the list.
const PASSWORD = 'Password123!@#';
const RECEIPT = path.join(process.cwd(), 'fixtures/demo-receipt.png');

async function registerAndLogin(page: Page) {
  const email = `e2e-tags-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`;
  await page.goto('/register');
  await page.getByLabel('Display name').fill('E2E Tags User');
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

test.describe('tags', () => {
  test('create, assign, filter, unassign and delete a tag', async ({ page }) => {
    await registerAndLogin(page);
    await createWorkspace(page, 'E2E Tags');

    await page.goto('/documents');
    const workspaceSelect = page.getByLabel('Select workspace');
    if (await workspaceSelect.isVisible()) {
      await workspaceSelect.selectOption({ label: 'E2E Tags' });
    }
    await expect(page.getByText('Documents in E2E Tags')).toBeVisible();

    // Create two tags in the manager.
    await page.getByLabel('New tag name').fill('Tax 2026');
    await page.getByRole('button', { name: 'Create tag' }).click();
    await expect(page.locator('.tag-manager', { hasText: 'Tax 2026' })).toBeVisible();
    await page.getByLabel('New tag name').fill('Receipts');
    await page.getByRole('button', { name: 'Create tag' }).click();
    await expect(page.locator('.tag-manager', { hasText: 'Receipts' })).toBeVisible();

    // A duplicate name is rejected with a readable error.
    await page.getByLabel('New tag name').fill('Tax 2026');
    await page.getByRole('button', { name: 'Create tag' }).click();
    await expect(page.getByText('unique')).toBeVisible();

    // Upload a document and wait for its row (OCR is not needed for tags).
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

    // Assign "Tax 2026" from the details panel; the row shows the chip.
    await row.getByRole('button', { name: 'Details' }).click();
    const addSelect = page.getByLabel('Add a tag');
    await addSelect.selectOption({ label: 'Tax 2026' });
    await page.getByRole('button', { name: 'Add tag' }).click();
    await expect(row.locator('.doc-tags', { hasText: 'Tax 2026' })).toBeVisible();

    // Filter by "Tax 2026": the row stays. Filter by "Receipts": it goes.
    const filter = page.getByLabel('Filter by tag');
    await filter.selectOption({ label: 'Tax 2026' });
    await expect(row).toBeVisible();
    await filter.selectOption({ label: 'Receipts' });
    await expect(row).toHaveCount(0);
    await filter.selectOption({ label: 'All tags' });
    await expect(row).toBeVisible();

    // Remove the tag again; the row chip disappears. The details panel
    // stayed open across the filter reloads (expandedId persists), so
    // there is no second Details click here.
    await page
      .getByRole('button', { name: 'Remove tag Tax 2026 from demo-receipt.png' })
      .click();
    await expect(row.locator('.doc-tags')).toHaveCount(0);

    // Delete the "Receipts" tag from the manager.
    await page.getByRole('button', { name: 'Delete tag Receipts' }).click();
    await expect(
      page.locator('.tag-manager', { hasText: 'Receipts' }),
    ).toHaveCount(0);
  });
});
