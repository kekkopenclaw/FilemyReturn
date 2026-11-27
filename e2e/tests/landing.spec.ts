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

  test('showcase is a real video, expenses show claimable totals, and the text is not glued', async ({ page }) => {
    await page.goto('/');

    // The showcase plays a REAL video of the scan demo — a looping,
    // muted, autoplaying <video>, not a static card. Scroll it into view
    // first, the way a visitor reaches it.
    const video = page.locator('.showcase-video');
    await video.scrollIntoViewIfNeeded();
    await expect(video).toBeVisible();
    await expect
      .poll(
        async () =>
          video.evaluate((el) => {
            const v = el as HTMLVideoElement;
            return { paused: v.paused, time: v.currentTime, ready: v.readyState };
          }),
        { timeout: 15_000 },
      )
      .toMatchObject({ paused: false, ready: expect.any(Number) });
    const state = await video.evaluate((el) => {
      const v = el as HTMLVideoElement;
      return { paused: v.paused, time: v.currentTime, ready: v.readyState };
    });
    expect(state.paused).toBe(false);
    expect(state.time).toBeGreaterThan(0);
    expect(state.ready).toBeGreaterThanOrEqual(2);

    // The page has ONE receipt: the hero Tesco receipt. The showcase
    // holds the video, not a second static receipt.
    await expect(page.getByText('TESCO EXTRA', { exact: true })).toHaveCount(1);

    // Expenses strip: sample expenses with claimable flags and the total.
    await expect(page.getByText('Receipts in, expenses out')).toBeVisible();
    await expect(page.getByText('£160.20 ready to claim')).toBeVisible();
    await expect(page.locator('.expense-strip').getByText('Claimable')).toHaveCount(2);
    await expect(page.getByText('check with your accountant')).toBeVisible();

    // The "Personal" flags must actually READ: plain .badge inherited
    // Bootstrap's white text on a near-white pill (invisible at 2×
    // zoom). badge-neutral sets dark text — check the contrast, not
    // just that the element exists.
    const personal = page.locator('.expense-strip', { hasText: 'Personal' }).getByText('Personal').first();
    const contrast = await personal.evaluate((el) => {
      const cs = getComputedStyle(el);
      const lum = (rgb: string): number => {
        const [r, g, b] = rgb.match(/\d+(\.\d+)?/g)!.slice(0, 3).map(Number).map((v) => {
          const s = v / 255;
          return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
        });
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
      };
      const l1 = lum(cs.color);
      const l2 = lum(cs.backgroundColor);
      const [hi, lo] = l1 > l2 ? [l1, l2] : [l2, l1];
      return (hi + 0.05) / (lo + 0.05);
    });
    expect(contrast).toBeGreaterThanOrEqual(4.5);

    // The glued text is fixed with real spaces in the templates — the
    // space sits INSIDE the span, so even a naive text extraction that
    // drops whitespace between elements reads it correctly.
    await expect(page.getByRole('heading', { name: 'Start filing in seconds.' })).toBeVisible();
    const ctaText = await page.locator('#cta-heading').textContent();
    expect(ctaText).toContain('Start filing in seconds.');
    expect(ctaText).not.toContain('inseconds');
    const shoeboxText = await page.locator('.shoebox').textContent();
    expect(shoeboxText).toContain('shoeboxes of paper');
    expect(shoeboxText).not.toContain('0shoeboxes');
    const demoTitle = await page.locator('.demo-title').textContent();
    expect(demoTitle).toContain('Watch me scan Interactive preview');
    // No non-breaking space gluing the words together.
    expect(demoTitle).not.toContain('\u00a0');
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
      return { bg: cs.backgroundColor, color: cs.color, deco: cs.textDecorationLine };
    });
    expect(startedStyle.bg).toBe('rgb(16, 26, 23)');
    expect(startedStyle.deco).toBe('none');
    // White text on the dark pill — the old dark-green-on-black was
    // unreadable (the component's `a{color}` rule outranked the global
    // button colour until it was scoped to `a:not(.btn)`).
    expect(startedStyle.color).toBe('rgb(255, 255, 255)');

    // The final call-to-action button has the same fix: dark ink text
    // on the green pill, white text on the dark one.
    const cta = page.getByRole('link', { name: 'Get started', exact: true }).last();
    await cta.scrollIntoViewIfNeeded();
    const ctaStyle = await cta.evaluate((el) => {
      const cs = getComputedStyle(el);
      return { bg: cs.backgroundColor, color: cs.color };
    });
    expect(ctaStyle.bg).toBe('rgb(14, 124, 102)');
    expect(ctaStyle.color).toBe('rgb(255, 255, 255)');

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
    // the scan completes. Sample the receipt's position over 2.5s — the
    // -28px / 4.5s float covers a wide range in that window, so a
    // >8px range proves it moves (a two-point check can catch the same
    // phase by luck and read ~0px even while it bobs).
    await expect(page.getByRole('status')).toContainText('Done. Filed and searchable', { timeout: 20_000 });
    const ys: number[] = [];
    for (let i = 0; i < 6; i++) {
      ys.push(await receipt.evaluate((el) => el.getBoundingClientRect().y));
      await page.waitForTimeout(500);
    }
    expect(Math.max(...ys) - Math.min(...ys)).toBeGreaterThan(8);

    // All five chips are FULLY visible around the receipt — in front of
    // it (not hidden behind), and fully inside the phone viewport (not
    // cut off at the screen edges). Scroll the whole scene into view
    // first: the test scrolled to the call-to-action earlier, and chips
    // below the fold would (correctly) fail the in-viewport check.
    await page.locator('.scene').scrollIntoViewIfNeeded();
    const chips = await page.evaluate(() => {
      const vw = window.innerWidth;
      const vh = window.innerHeight;
      return ['.chip-a', '.chip-b', '.chip-c', '.chip-d', '.chip-e'].map((sel) => {
        const el = document.querySelector(sel) as HTMLElement;
        const r = el.getBoundingClientRect();
        const cx = r.x + r.width / 2;
        const cy = r.y + r.height / 2;
        const top = document.elementFromPoint(cx, cy);
        return {
          sel,
          inViewport: r.x >= 0 && r.y >= 0 && r.x + r.width <= vw && r.y + r.height <= vh,
          onTop: !!top && (top === el || el.contains(top as Node)),
        };
      });
    });
    for (const chip of chips) {
      expect(chip.inViewport, `${chip.sel} inside the viewport`).toBe(true);
      expect(chip.onTop, `${chip.sel} on top of the receipt`).toBe(true);
    }

    // The chips must not collide with each other: no two chip boxes
    // overlap (Vendor + Date used to sit on top of each other across
    // the receipt header).
    const overlaps = await page.evaluate(() => {
      const rects = ['.chip-a', '.chip-b', '.chip-c', '.chip-d', '.chip-e'].map((sel) => {
        const r = (document.querySelector(sel) as HTMLElement).getBoundingClientRect();
        return { sel, x: r.x, y: r.y, w: r.width, h: r.height };
      });
      const bad: string[] = [];
      for (let i = 0; i < rects.length; i++) {
        for (let j = i + 1; j < rects.length; j++) {
          const a = rects[i];
          const b = rects[j];
          const overlapX = Math.min(a.x + a.w, b.x + b.w) - Math.max(a.x, b.x);
          const overlapY = Math.min(a.y + a.h, b.y + b.h) - Math.max(a.y, b.y);
          if (overlapX > 2 && overlapY > 2) bad.push(`${a.sel} overlaps ${b.sel}`);
        }
      }
      return bad;
    });
    expect(overlaps).toEqual([]);

    // The chips must not cover the receipt's text: the shop header and
    // the line items stay readable — whatever is at their centre point
    // belongs to the receipt, not a chip.
    const covered = await page.evaluate(() => {
      const insideReceipt = (el: Element | null): boolean =>
        !!el && !!el.closest('.receipt');
      const points: { name: string; el: Element | null }[] = [
        { name: 'header', el: document.elementFromPoint(
          (() => { const r = document.querySelector('.r-shop')!.getBoundingClientRect(); return r.x + r.width / 2; })(),
          (() => { const r = document.querySelector('.r-shop')!.getBoundingClientRect(); return r.y + r.height / 2; })(),
        ) },
      ];
      document.querySelectorAll('.r-items li').forEach((li, i) => {
        if (i % 2 === 0) {
          const r = li.getBoundingClientRect();
          points.push({ name: `item-${i}`, el: document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2) });
        }
      });
      return points.filter((p) => !insideReceipt(p.el)).map((p) => p.name);
    });
    expect(covered).toEqual([]);

    // The chips persist through the loop: once the scan restarts, the
    // extraction panel re-reads one by one — but the chips must stay
    // visible the whole cycle, not vanish until the end.
    const status = page.getByRole('status');
    await expect(status).toContainText('Reading receipt', { timeout: 15_000 });
    const chipOpacity = await page.evaluate(() =>
      ['.chip-a', '.chip-b', '.chip-c', '.chip-d', '.chip-e'].map((sel) =>
        getComputedStyle(document.querySelector(sel) as HTMLElement).opacity,
      ),
    );
    expect(chipOpacity).toEqual(['1', '1', '1', '1', '1']);

    // No horizontal overflow on a phone: nothing is cut off at the edges.
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    );
    expect(overflow).toBe(true);

    // The shoebox stat counts down to 0 when it scrolls into view.
    await expect(page.locator('.shoebox')).toContainText('0 shoeboxes of paper', { timeout: 5_000 });
  });
});
