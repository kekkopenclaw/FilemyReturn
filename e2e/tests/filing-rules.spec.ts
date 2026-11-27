import path from 'node:path';
import { test, expect, type Page } from '@playwright/test';

// Phase 4 organisation: filing rules file documents automatically. Create a
// rule (vendor contains + assign tag + set category + move to archive), upload
// a receipt, apply the rules manually from the document row, and check the
// tag lands on the row and the rule's applied count ticks up. The automatic
// run after OCR is covered by the backend FilingRuleTests (OCR needs
// tesseract, which this VM does not have).
const PASSWORD = 'Password123!@#';
const RECEIPT = path.join(process.cwd(), 'fixtures/demo-receipt.png');

async function registerAndLogin(page: Page) {
  const email = `e2e-rules-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`;
  await page.goto('/register');
  await page.getByLabel('Display name').fill('E2E Rules User');
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

async function selectWorkspace(page: Page, name: string) {
  const workspaceSelect = page.getByLabel('Select workspace');
  if (await workspaceSelect.isVisible()) {
    await workspaceSelect.selectOption({ label: name });
  }
}

test.describe('filing rules', () => {
  test('create a rule, upload a receipt, apply the rule, tag lands', async ({
    page,
  }) => {
    await registerAndLogin(page);
    await createWorkspace(page, 'E2E Rules');

    // An archive for the rule to move documents into.
    await page.goto('/archives');
    await selectWorkspace(page, 'E2E Rules');
    await page.getByLabel('Name').fill('Filed 2026');
    await page.getByRole('button', { name: 'Create archive' }).click();
    await expect(page.getByText('Archive created.')).toBeVisible();

    // A tag for the rule to assign.
    await page.goto('/documents');
    await selectWorkspace(page, 'E2E Rules');
    await expect(page.getByText('Documents in E2E Rules')).toBeVisible();
    await page.getByLabel('New tag name').fill('Expenses');
    await page.getByRole('button', { name: 'Create tag' }).click();
    await expect(page.locator('.tag-manager', { hasText: 'Expenses' })).toBeVisible();

    // Upload a receipt and wait for a terminal state (Ready, or Failed when
    // the VM has no tesseract) so the list stops polling every 5s.
    const uploadInput = page.locator('#document-upload');
    await expect(uploadInput).toBeEnabled();
    await uploadInput.setInputFiles(RECEIPT);
    await expect(page.getByText('Document uploaded.')).toBeVisible();
    const row = page.locator('tr', { hasText: 'demo-receipt.png' });
    await expect(row).toBeVisible();
    await expect(row.getByText(/^(Ready|Failed)$/)).toBeVisible({
      timeout: 120_000,
    });

    // Create the rule: file name contains "demo-receipt" → tag Expenses,
    // category Receipt, move to "Filed 2026". (The vendor condition is
    // covered by the backend FilingRuleTests — this VM has no tesseract, so
    // the uploaded document has no extracted vendor to match on.)
    await page.goto('/filing-rules');
    await expect(
      page.getByRole('heading', { name: 'Filing rules' }),
    ).toBeVisible();
    await page.getByLabel('Rule name').fill('Receipt scans');
    await page.getByLabel('File name contains').fill('demo-receipt');
    await page.getByLabel('Assign tag').selectOption({ label: 'Expenses' });
    await page.getByLabel('Set category').selectOption({ label: 'Receipt' });
    await page
      .getByLabel('Move to archive')
      .selectOption({ label: 'Filed 2026' });
    await page.getByRole('button', { name: 'Create rule' }).click();
    await expect(page.getByText('Rule created.')).toBeVisible();
    const ruleRow = page.locator('.rule-row', { hasText: 'Receipt scans' });
    await expect(ruleRow).toBeVisible();
    await expect(ruleRow).toContainText('Not applied yet');

    // A rule with no condition is refused by the form.
    await page.getByLabel('Rule name').fill('No condition');
    await page.getByRole('button', { name: 'Create rule' }).click();
    await expect(page.getByText('at least one condition')).toBeVisible();

    // Apply the rules from the document row: the tag chip lands on the row.
    await page.goto('/documents');
    const rowAfter = page.locator('tr', { hasText: 'demo-receipt.png' });
    await expect(rowAfter).toBeVisible();
    await rowAfter
      .getByRole('button', { name: 'Apply filing rules' })
      .click();
    await expect(rowAfter.locator('.doc-tags', { hasText: 'Expenses' })).toBeVisible();

    // The rule now reports one application.
    await page.goto('/filing-rules');
    await expect(
      page.locator('.rule-row', { hasText: 'Receipt scans' }),
    ).toContainText('Applied once');
  });
});

test.describe('filing rules on a phone', () => {
  test.use({ viewport: { width: 390, height: 844 } });

  test('the rules page fits a 390px viewport with no horizontal overflow', async ({
    page,
  }) => {
    await registerAndLogin(page);
    await createWorkspace(page, 'E2E Rules Phone');
    await page.goto('/filing-rules');
    await expect(page.getByLabel('Rule name')).toBeVisible();
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBe(0);
  });
});
