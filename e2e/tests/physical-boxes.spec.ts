import path from 'node:path';
import { test, expect, type Page } from '@playwright/test';

// Phase 7 physical archive: create a box, file an uploaded document in it,
// see the QR code and the document in the box contents, open the box by its
// QR deep link, delete the box and check the document survives — through the
// real UI against the local app. Filing does not need OCR, so the document
// only has to reach a terminal state (Ready, or Failed where the VM has no
// tesseract) before the row stops being replaced by the 5s poll.
const PASSWORD = 'Password123!@#';
const RECEIPT = path.join(process.cwd(), 'fixtures/demo-receipt.png');

async function registerAndLogin(page: Page) {
  const email = `e2e-boxes-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`;
  await page.goto('/register');
  await page.getByLabel('Display name').fill('E2E Boxes User');
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

test.describe('physical boxes', () => {
  test('create a box, file a document, QR, deep link, delete keeps the document', async ({
    page,
  }) => {
    await registerAndLogin(page);
    await createWorkspace(page, 'E2E Boxes');

    // Create a box on the Boxes page.
    await page.goto('/boxes');
    await expect(
      page.getByRole('heading', { name: 'Physical boxes' }),
    ).toBeVisible();
    await page.getByLabel('Box name').fill('Tax archive 2025');
    await page.getByLabel('Location').fill('Shelf B, row 3');
    await page.getByRole('button', { name: 'Create box' }).click();
    await expect(page.getByText(/Box created/)).toBeVisible();
    const boxRow = page.locator('.box-row', { hasText: 'Tax archive 2025' });
    await expect(boxRow).toBeVisible();
    const code = (await boxRow.locator('.box-code').textContent()) ?? '';
    expect(code).toMatch(/^BOX-[A-Z0-9]{6}$/);
    await expect(boxRow.getByText('Shelf B, row 3')).toBeVisible();

    // A duplicate name in the same workspace is rejected.
    await page.getByLabel('Box name').fill('Tax archive 2025');
    await page.getByLabel('Location').fill('Shelf B, row 3');
    await page.getByRole('button', { name: 'Create box' }).click();
    await expect(page.getByText('unique')).toBeVisible();

    // Upload a document and wait for its row to reach a terminal state.
    await page.goto('/documents');
    const workspaceSelect = page.getByLabel('Select workspace');
    if (await workspaceSelect.isVisible()) {
      await workspaceSelect.selectOption({ label: 'E2E Boxes' });
    }
    await expect(page.getByText('Documents in E2E Boxes')).toBeVisible();
    const uploadInput = page.locator('#document-upload');
    await expect(uploadInput).toBeEnabled();
    await uploadInput.setInputFiles(RECEIPT);
    await expect(page.getByText('Document uploaded.')).toBeVisible();
    const row = page.locator('tr', { hasText: 'demo-receipt.png' });
    await expect(row).toBeVisible();
    await expect(row.getByText(/^(Ready|Failed)$/)).toBeVisible({
      timeout: 120_000,
    });

    // File the document in the box from the details panel.
    await row.getByRole('button', { name: 'Details' }).click();
    const boxSelect = page.getByLabel('File in a box');
    await boxSelect.selectOption({ index: 1 });
    await page.getByRole('button', { name: 'File in box' }).click();
    await expect(
      row.locator('.doc-tags', { hasText: 'Box: Tax archive 2025' }),
    ).toBeVisible();

    // The box contents list the document and show its QR code.
    await page.goto('/boxes');
    await page
      .getByRole('button', { name: 'View contents — Tax archive 2025' })
      .click();
    await expect(page.getByText('demo-receipt.png')).toBeVisible();
    await expect(page.locator('.qr-image svg')).toBeVisible();
    await expect(
      page.getByRole('button', { name: 'Print label' }),
    ).toBeVisible();

    // The QR deep link opens the same box directly.
    await page.goto(`/boxes?box=${code}`);
    await expect(page.getByText('demo-receipt.png')).toBeVisible();
    await expect(page.locator('.qr-image svg')).toBeVisible();

    // Deleting the box keeps the document, without its box chip.
    await page
      .getByRole('button', { name: 'Delete box Tax archive 2025' })
      .click();
    await expect(
      page.locator('.box-row', { hasText: 'Tax archive 2025' }),
    ).toHaveCount(0);
    await page.goto('/documents');
    await expect(
      page.locator('tr', { hasText: 'demo-receipt.png' }),
    ).toBeVisible();
    await expect(
      page.locator('tr', { hasText: 'demo-receipt.png' }).locator('.doc-tags', {
        hasText: 'Box: Tax archive 2025',
      }),
    ).toHaveCount(0);
  });

  test('boxes page fits a 390px phone with no horizontal overflow', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await registerAndLogin(page);
    await createWorkspace(page, 'E2E Boxes Phone');
    await page.goto('/boxes');
    await page.getByLabel('Box name').fill('Loft boxes');
    await page.getByLabel('Location').fill('Loft, north wall');
    await page.getByRole('button', { name: 'Create box' }).click();
    await expect(
      page.locator('.box-row', { hasText: 'Loft boxes' }),
    ).toBeVisible();
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    );
    expect(overflow).toBe(true);
  });
});
