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

  test('scan loops on its own — no click needed', async ({ page }) => {
    await page.goto('/');
    const demo = page.locator('#demo-heading');
    await demo.scrollIntoViewIfNeeded();

    // The demo auto-plays (on load or on scroll) and loops: filed state,
    // then it reads the receipt again on its own.
    const status = page.getByRole('status');
    await expect(status).toContainText('Done. Filed and searchable', { timeout: 20_000 });
    await expect(status).toContainText('Reading receipt', { timeout: 15_000 });
    await expect(status).toContainText('Done. Filed and searchable', { timeout: 20_000 });
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

  test('on a phone the buttons are styled, the chips float around the receipt, and the receipt keeps moving', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/');
    await page.locator('#demo-heading').scrollIntoViewIfNeeded();

    // Styled green pill buttons, not native grey buttons or blue links.
    const watch = page.getByRole('button', { name: 'Watch it scan a receipt' });
    await expect(watch).toBeVisible();
    const watchStyle = await watch.evaluate((el) => {
      const cs = getComputedStyle(el);
      return { bg: cs.backgroundColor, radius: cs.borderRadius, deco: cs.textDecorationLine };
    });
    expect(watchStyle.bg).toBe('rgb(14, 124, 102)');
    expect(watchStyle.radius).toBe('999px');
    expect(watchStyle.deco).toBe('none');

    const started = page.getByRole('link', { name: 'Get started' }).first();
    const startedStyle = await started.evaluate((el) => {
      const cs = getComputedStyle(el);
      return { bg: cs.backgroundColor, deco: cs.textDecorationLine };
    });
    expect(startedStyle.bg).toBe('rgb(16, 26, 23)');
    expect(startedStyle.deco).toBe('none');

    // The receipt has the serrated torn edge top and bottom.
    const receipt = page.locator('.receipt');
    const edges = await receipt.evaluate((el) => {
      const before = getComputedStyle(el, '::before');
      const after = getComputedStyle(el, '::after');
      return {
        before: before.backgroundImage,
        after: after.backgroundImage,
        anim: getComputedStyle(el).animationName,
      };
    });
    expect(edges.before).toContain('data:image/svg+xml');
    expect(edges.after).toContain('data:image/svg+xml');
    // Angular scopes keyframe names per component, so the running
    // animation is named "<scope>_float" — the float is what matters.
    expect(edges.anim).toContain('_float');

    // The chips are absolutely positioned AROUND the receipt (not a
    // static stacked row): one near the top-left, one near the bottom.
    const boxes = await page.evaluate(() => {
      const r = document.querySelector('.receipt')!.getBoundingClientRect();
      const a = document.querySelector('.chip-a')!.getBoundingClientRect();
      const c = document.querySelector('.chip-c')!.getBoundingClientRect();
      const csA = getComputedStyle(document.querySelector('.chip-a')!);
      return {
        receipt: { x: r.x, y: r.y, h: r.height },
        a: { x: a.x, y: a.y, pos: csA.position },
        c: { x: c.x, y: c.y },
      };
    });
    expect(boxes.a.pos).toBe('absolute');
    expect(boxes.a.x).toBeLessThan(boxes.receipt.x);
    expect(boxes.a.y).toBeLessThan(boxes.receipt.y + 100);
    expect(boxes.c.y).toBeGreaterThan(boxes.receipt.y + boxes.receipt.h * 0.5);

    // The finished state stays alive: the receipt is still bobbing after
    // the scan completes.
    await expect(page.getByRole('status')).toContainText('Done. Filed and searchable', { timeout: 20_000 });
    const y1 = await receipt.evaluate((el) => el.getBoundingClientRect().y);
    await page.waitForTimeout(1500);
    const y2 = await receipt.evaluate((el) => el.getBoundingClientRect().y);
    expect(Math.abs(y2 - y1)).toBeGreaterThan(2);
  });
});
