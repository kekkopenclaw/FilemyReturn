import { test, expect } from '@playwright/test';

// Landing page: the public home page, the "Watch me scan" receipt demo and
// the real Log in / Get started buttons. Style-agnostic: these tests check
// the demo works, not which colours it uses (the design is being restyled).
test.describe('landing page', () => {
  test('shows the hero and the scan demo', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByText('Lost a receipt again?')).toBeVisible();
    await expect(page.getByText('Scan it. Done.')).toBeVisible();
    await expect(page.getByText('Watch me scan')).toBeVisible();

    // The demo receipt is on the page (Tesco Extra, TOTAL £47.82).
    await expect(page.getByText('TESCO EXTRA', { exact: true }).first()).toBeVisible();
    await expect(page.getByText('24 Nov 2026').first()).toBeVisible();
    await expect(page.getByText('£47.82').first()).toBeVisible();
  });

  test('scan button reveals the extracted fields one by one', async ({ page }) => {
    await page.goto('/');
    const demo = page.locator('#demo-heading');
    await demo.scrollIntoViewIfNeeded();

    // The demo may auto-play on scroll; either way the button replays it.
    const scanButton = page.getByRole('button', { name: /Scan (this receipt|it again)/ });
    await expect(scanButton).toBeVisible();
    await scanButton.click();

    // All six extracted fields appear with their values, in the
    // extraction panel beside the 3D receipt. The <dd> holds the value
    // plus a tick, so match by substring inside .demo-extracted.
    const extracted = page.locator('.demo-extracted');
    await expect(extracted.locator('dd', { hasText: 'Tesco Extra' })).toBeVisible();
    await expect(extracted.locator('dd', { hasText: '24 Nov 2026' })).toBeVisible();
    await expect(extracted.locator('dd', { hasText: '£47.82' })).toBeVisible();
    await expect(extracted.locator('dd', { hasText: '£4.18' })).toBeVisible();
    await expect(extracted.locator('.cat-pill', { hasText: 'Groceries' })).toBeVisible();
    await expect(extracted.locator('dd', { hasText: 'Expenses / Nov 2026' })).toBeVisible();
    await expect(page.getByRole('status')).toContainText('Done. Filed and searchable', { timeout: 20_000 });
  });

  test('login and register buttons go to real pages', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('link', { name: 'Log in' }).first().click();
    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByLabel('Email')).toBeVisible();

    await page.goto('/');
    await page.getByRole('link', { name: 'Get started' }).first().click();
    await expect(page).toHaveURL(/\/register$/);
    await expect(page.getByLabel('Email')).toBeVisible();
  });
});
