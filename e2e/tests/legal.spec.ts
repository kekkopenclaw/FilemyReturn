import { test, expect } from '@playwright/test';

// Phase 10 legal pages: public Terms of Service and Privacy Policy at
// /terms and /privacy, linked from the landing-page footer. No sign-in
// needed — these are the pages a visitor reads before registering. Runs
// against the local app only — never the live site.
test.describe('legal pages', () => {
  test('terms page loads with the real content and links to privacy', async ({
    page,
  }) => {
    await page.goto('/terms');
    await expect(
      page.getByRole('heading', { name: 'Terms of Service' }),
    ).toBeVisible();
    await expect(page.getByText('Last updated: 27 November 2026')).toBeVisible();
    await expect(
      page.getByText('not sent to an outside AI service'),
    ).toBeVisible();
    // No invented contact details — the contact is a placeholder.
    await expect(
      page.getByText('contact details to be added by the site owner'),
    ).toBeVisible();
    await page.getByRole('link', { name: 'Privacy Policy' }).click();
    await expect(page).toHaveURL(/\/privacy$/);
  });

  test('privacy page loads with the real content and links to terms', async ({
    page,
  }) => {
    await page.goto('/privacy');
    await expect(
      page.getByRole('heading', { name: 'Privacy Policy' }),
    ).toBeVisible();
    await expect(page.getByText('Last updated: 27 November 2026')).toBeVisible();
    await expect(
      page.getByText('We do not sell your personal data'),
    ).toBeVisible();
    await expect(
      page.getByText('contact details to be added by the site owner'),
    ).toBeVisible();
    await page.getByRole('link', { name: 'Terms of Service' }).click();
    await expect(page).toHaveURL(/\/terms$/);
  });

  test('landing footer links reach both legal pages', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('link', { name: 'Terms of Service' }).click();
    await expect(page).toHaveURL(/\/terms$/);
    await expect(
      page.getByRole('heading', { name: 'Terms of Service' }),
    ).toBeVisible();
    await page.goto('/');
    await page.getByRole('link', { name: 'Privacy Policy' }).click();
    await expect(page).toHaveURL(/\/privacy$/);
    await expect(
      page.getByRole('heading', { name: 'Privacy Policy' }),
    ).toBeVisible();
  });

  test('both legal pages fit a phone screen', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    for (const path of ['/terms', '/privacy']) {
      await page.goto(path);
      await expect(page.getByRole('heading').first()).toBeVisible();
      const overflow = await page.evaluate(
        () =>
          document.documentElement.scrollWidth -
          document.documentElement.clientWidth,
      );
      expect(overflow).toBe(0);
    }
  });
});
