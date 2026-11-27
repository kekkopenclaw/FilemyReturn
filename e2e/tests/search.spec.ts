import path from 'node:path';
import { test, expect, type Page } from '@playwright/test';

// Phase 5 search: free text over file name, vendor and OCR text plus
// filters, with the matched terms highlighted in the snippet. The
// file-name search works without OCR, so it is asserted unconditionally;
// the OCR-term assertions only run when the document actually reached
// Ready (tesseract is not always available in the dev VM).
const PASSWORD = 'Password123!@#';
const RECEIPT = path.join(process.cwd(), 'fixtures/demo-receipt.png');

async function registerAndLogin(page: Page) {
  const email = `e2e-search-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`;
  await page.goto('/register');
  await page.getByLabel('Display name').fill('E2E Search User');
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

async function searchFor(page: Page, text: string) {
  await page.getByLabel('Search text').fill(text);
  await page.getByRole('button', { name: 'Search', exact: true }).click();
}

test.describe('search', () => {
  test('search by file name finds the document with a highlighted snippet', async ({
    page,
  }) => {
    await registerAndLogin(page);
    await createWorkspace(page, 'E2E Search');

    await page.goto('/documents');
    const workspaceSelect = page.getByLabel('Select workspace');
    if (await workspaceSelect.isVisible()) {
      await workspaceSelect.selectOption({ label: 'E2E Search' });
    }
    await expect(page.getByText('Documents in E2E Search')).toBeVisible();

    const uploadInput = page.locator('#document-upload');
    await expect(uploadInput).toBeEnabled();
    await uploadInput.setInputFiles(RECEIPT);
    await expect(page.getByText('Document uploaded.')).toBeVisible();
    const row = page.locator('tr', { hasText: 'demo-receipt.png' });
    await expect(row).toBeVisible();
    // Wait for a terminal state (Ready, or Failed when the VM has no
    // tesseract) so the list stops polling every 5s.
    await expect(row.getByText(/^(Ready|Failed)$/)).toBeVisible({
      timeout: 120_000,
    });
    const ocrRan = (await row.getByText('Ready').count()) > 0;

    await page.goto('/search');
    await expect(page.getByRole('heading', { name: 'Search documents' })).toBeVisible();

    // No criteria: the API is not called and the error shows.
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    await expect(
      page.getByText('Give the search something to look for'),
    ).toBeVisible();

    // File-name search works with or without OCR.
    await searchFor(page, 'demo-receipt');
    await expect(page.getByText('1 document found')).toBeVisible();
    const card = page.locator('.result-card', {
      hasText: 'demo-receipt.png',
    });
    await expect(card).toBeVisible();
    await expect(card.getByText('Matched in File name')).toBeVisible();
    await expect(card.locator('mark', { hasText: 'demo-receipt' })).toBeVisible();

    if (ocrRan) {
      // OCR text search: "flat white" is on the demo receipt.
      await searchFor(page, 'flat white');
      await expect(page.getByText('1 document found')).toBeVisible();
      const ocrCard = page.locator('.result-card', {
        hasText: 'demo-receipt.png',
      });
      await expect(ocrCard.getByText('Matched in OCR text')).toBeVisible();
      await expect(
        ocrCard.locator('mark', { hasText: 'flat' }),
      ).toBeVisible();

      // Category filter: the receipt classifies as Receipt.
      await page.getByLabel('Category').selectOption('Receipt');
      await searchFor(page, 'flat white');
      await expect(page.getByText('1 document found')).toBeVisible();
      await page.getByLabel('Category').selectOption('Invoice');
      await searchFor(page, 'flat white');
      await expect(page.getByText('No documents match')).toBeVisible();
    }

    // Phone width: the search form fits with no horizontal overflow.
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/search');
    await expect(page.getByLabel('Search text')).toBeVisible();
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBe(0);
  });
});
