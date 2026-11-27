import {
  AfterViewInit,
  Component,
  ElementRef,
  OnDestroy,
  computed,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Public landing page (DocuArchive home page) — the futuristic 3D cream
 * design, approved by Francesco 2026-11-27 as the live landing.
 *
 * Purely presentational: every button is a real link (log in, register, or
 * an on-page section), except the scan demo, which runs a client-side
 * animation over sample receipt data. A route guard keeps signed-in users
 * out — they go to /dashboard instead.
 *
 * The demo uses the Tesco Extra receipt from the old demo (TOTAL £47.82,
 * 24 Nov 2026, 18:42). The scan makes it explicit the software is
 * READING: each field is announced as it is read off the receipt, in
 * order — Shop, Date, Total, VAT, Category, Filed to — then the receipt
 * is filed automatically. The scan LOOPS on its own (hold the filed state
 * a few seconds, then replay) — a visitor never has to click "Scan
 * again". The showcase below the hero is a REAL VIDEO of the scan demo
 * (recorded with Playwright from this page, looping, muted, autoplay),
 * with the step captions alongside. The dashboard glimpse is followed by
 * a sample expenses strip: scanned receipts become expenses you can
 * claim, with a claimable total (sample data only, with an accountant
 * note — never tax advice). With reduced motion both animations skip
 * straight to the filed state.
 *
 * The scene is 3D INTERACTIVE, not just animated: the receipt tilts with
 * the mouse on desktop and with the phone's gyro on mobile (iOS asks for
 * motion permission from a tap on the scene), and the background grid,
 * orbit rings, receipt, chips and HUD move at different rates with the
 * pointer (parallax depth). Chips lift and glow on hover/touch.
 */
@Component({
  imports: [RouterLink],
  selector: 'app-landing',
  styleUrl: './landing.css',
  templateUrl: './landing.html',
})
export class Landing implements AfterViewInit, OnDestroy {
  protected readonly scanning = signal(false);
  protected readonly done = signal(false);
  protected readonly revealed = signal(0);
  /**
   * The chips' reveal state — unlike `revealed`, this NEVER resets. The
   * scan loop re-reads the fields one by one each cycle (the extraction
   * panel replays the reading), but the chips, once read, stay visible:
   * resetting them made them vanish for most of the ~30s cycle and only
   * flash at the end. A chip is revealed the first time its field is
   * read and stays revealed forever after.
   */
  protected readonly chipsRevealed = signal(0);
  protected readonly status = signal(
    'Press "Scan this receipt" — the software reads the receipt live.',
  );
  protected readonly rx = signal(-10);
  protected readonly ry = signal(14);
  // "0 shoeboxes of paper": the count animates when it scrolls into
  // view — shoeboxes empty into the archive, 9 down to 0 — so the stat
  // is alive instead of a dead-looking 0. Reduced motion shows 0 at once.
  protected readonly shoeboxes = signal(0);
  // Sample expenses (the dashboard's sample documents, tagged): scanned
  // receipts become expenses you can claim. Sample data only — what
  // anyone can actually claim depends on their situation.
  protected readonly expenses = [
    { name: 'Plumber invoice', kind: 'Home repairs', date: '18 Nov 2026', amount: 86.0, claimable: true },
    { name: 'Electricity bill', kind: 'Utilities', date: '12 Nov 2026', amount: 74.2, claimable: true },
    { name: 'Tesco Extra receipt', kind: 'Groceries', date: '24 Nov 2026', amount: 47.82, claimable: false },
    { name: 'The Corner Cafe receipt', kind: 'Eating out', date: '24 Nov 2026', amount: 18.4, claimable: false },
  ];
  protected readonly claimableTotal = computed(() =>
    this.expenses.filter((e) => e.claimable).reduce((sum, e) => sum + e.amount, 0),
  );
  protected readonly claimableTotalText = computed(
    () => `£${this.claimableTotal().toFixed(2)}`,
  );
  // Pointer position over the scene (-0.5..0.5 each axis) — drives the
  // parallax layers (grid floor, orbit rings, HUD) at different rates.
  protected readonly px = signal(0);
  protected readonly py = signal(0);
  protected readonly dashboardFlat = signal(false);
  protected readonly query = signal('');
  protected readonly filter = signal('All');
  protected readonly showcasePhase = signal(0);
  protected readonly showcaseRunning = signal(false);

  protected readonly filters = ['All', 'Ready', 'Processing', 'Awaiting OCR'];

  protected readonly docs = [
    { name: 'Tesco Extra receipt', kind: 'Groceries', date: '24 Nov 2026', amount: '£47.82', state: 'Ready' },
    { name: 'The Corner Cafe receipt', kind: 'Eating out', date: '24 Nov 2026', amount: '£18.40', state: 'Ready' },
    { name: 'Plumber invoice', kind: 'Home', date: '18 Nov 2026', amount: '£86.00', state: 'Ready' },
    { name: 'Electricity bill', kind: 'Utilities', date: '12 Nov 2026', amount: '£74.20', state: 'Awaiting OCR' },
    { name: 'HMRC letter', kind: 'Tax', date: '03 Nov 2026', amount: '—', state: 'Processing' },
  ];

  protected readonly filteredDocs = computed(() => {
    const q = this.query().trim().toLowerCase();
    const f = this.filter();
    return this.docs.filter((d) => {
      const matchesFilter = f === 'All' || d.state === f;
      const matchesQuery =
        !q ||
        `${d.name} ${d.kind} ${d.date} ${d.amount} ${d.state}`
          .toLowerCase()
          .includes(q);
      return matchesFilter && matchesQuery;
    });
  });

  protected readonly showcasePhases = [
    { title: 'Scan', text: 'The receipt goes in — no typing, no scanner bed, just a photo.' },
    { title: 'Read', text: 'The software reads it: shop, date, total and VAT, straight off the paper.' },
    { title: 'Categorise', text: 'Filed as Groceries — the category is worked out for you.' },
    { title: 'File', text: 'Into Expenses / Nov 2026 / Groceries, with the original kept safe.' },
    { title: 'Find', text: 'Search “Tesco” and it is there in 0.4 seconds.' },
  ];

  protected readonly showcaseCurrent = computed(
    () => this.showcasePhases[this.showcasePhase()],
  );

  private readonly scene = viewChild<ElementRef<HTMLElement>>('scene');
  private readonly glimpse = viewChild<ElementRef<HTMLElement>>('glimpse');
  private readonly showcase = viewChild<ElementRef<HTMLElement>>('showcase');
  private readonly demo = viewChild<ElementRef<HTMLElement>>('demo');
  private readonly shoebox = viewChild<ElementRef<HTMLElement>>('shoebox');
  private observers: IntersectionObserver[] = [];
  private timers: ReturnType<typeof setTimeout>[] = [];
  private shoeboxTimers: ReturnType<typeof setTimeout>[] = [];
  private showcaseTimers: ReturnType<typeof setTimeout>[] = [];
  private autoPlayed = false;
  private showcaseStarted = false;
  private shoeboxStarted = false;

  ngAfterViewInit(): void {
    if (typeof IntersectionObserver === 'undefined') {
      return;
    }
    // Autoplay the scan when the 3D scene itself scrolls into view.
    // (The whole figure is taller than a phone screen, so watching it
    // with a 0.4 threshold would never fire on mobile.)
    const sceneEl = this.scene()?.nativeElement;
    if (sceneEl) {
      const o = new IntersectionObserver(
        (entries) => {
          if (entries.some((e) => e.isIntersecting) && !this.autoPlayed) {
            this.autoPlayed = true;
            this.scan();
          }
        },
        { threshold: 0.2 },
      );
      o.observe(sceneEl);
      this.observers.push(o);
    }
    const glimpseEl = this.glimpse()?.nativeElement;
    if (glimpseEl) {
      const o = new IntersectionObserver(
        (entries) => {
          if (entries.some((e) => e.isIntersecting)) {
            this.dashboardFlat.set(true);
            o.disconnect();
          }
        },
        { threshold: 0.3 },
      );
      o.observe(glimpseEl);
      this.observers.push(o);
    }
    const showcaseEl = this.showcase()?.nativeElement;
    if (showcaseEl) {
      // The captions run on a free cycle from page load, in step with the
      // 12.68s video (5 phases × 2.54s) — they must not stall on step 1
      // while the video plays on. Starting on init (not only on scroll)
      // keeps them in sync with the video, which autoplays from load.
      this.startShowcase();
      const o = new IntersectionObserver(
        (entries) => {
          if (entries.some((e) => e.isIntersecting)) {
            this.startShowcase();
          }
        },
        { threshold: 0.2 },
      );
      o.observe(showcaseEl);
      this.observers.push(o);
    }
    // The shoebox stat counts down (9 → 0) the first time it scrolls
    // into view. Reduced motion shows the final 0 with no animation.
    const shoeboxEl = this.shoebox()?.nativeElement;
    if (shoeboxEl) {
      if (this.reducedMotion()) {
        this.shoeboxes.set(0);
      } else {
        const o = new IntersectionObserver(
          (entries) => {
            if (entries.some((e) => e.isIntersecting) && !this.shoeboxStarted) {
              this.shoeboxStarted = true;
              this.countShoeboxes();
              o.disconnect();
            }
          },
          { threshold: 0.5 },
        );
        o.observe(shoeboxEl);
        this.observers.push(o);
      }
    }
    // Device tilt (phone gyro) drives the same 3D tilt as the mouse.
    // On iOS the listener only fires after the user grants motion
    // permission — enableGyro() requests it from a tap on the scene.
    if (typeof window !== 'undefined' && 'DeviceOrientationEvent' in window) {
      window.addEventListener('deviceorientation', this.onDeviceOrientation);
    }
  }

  ngOnDestroy(): void {
    this.observers.forEach((o) => o.disconnect());
    this.timers.forEach((t) => clearTimeout(t));
    this.shoeboxTimers.forEach((t) => clearTimeout(t));
    this.showcaseTimers.forEach((t) => clearTimeout(t));
    if (typeof window !== 'undefined') {
      window.removeEventListener('deviceorientation', this.onDeviceOrientation);
    }
  }

  /** Hero button: scroll to the demo and run the scan. */
  protected watchScan(): void {
    const el = this.demo()?.nativeElement;
    const reduced = this.reducedMotion();
    el?.scrollIntoView({ behavior: reduced ? 'auto' : 'smooth', block: 'start' });
    this.scan();
  }

  protected scan(): void {
    if (this.scanning()) {
      return;
    }
    this.timers.forEach((t) => clearTimeout(t));
    this.timers = [];
    this.scanning.set(true);
    this.done.set(false);
    this.revealed.set(0);
    this.status.set('Reading receipt…');

    if (this.reducedMotion()) {
      this.finish();
      return;
    }

    // The scan must make it clear the software is READING: each field is
    // announced as it is read off the receipt, in order, ending with the
    // category and the filing — then the receipt is filed automatically.
    // reveal() moves both the panel (which replays every loop) and the
    // chips (which persist once read — see chipsRevealed).
    this.later(1200, () => this.status.set('Reading — shop found: Tesco Extra.'));
    this.later(1400, () => this.reveal(1));
    this.later(1800, () => this.status.set('Reading — date found: 24 Nov 2026, 18:42.'));
    this.later(2000, () => this.reveal(2));
    this.later(2400, () => this.status.set('Reading — total found: £47.82.'));
    this.later(2600, () => this.reveal(3));
    this.later(3000, () => this.status.set('Reading — VAT found: £4.18.'));
    this.later(3200, () => this.reveal(4));
    this.later(3600, () => this.status.set('Reading — category: Groceries.'));
    this.later(3800, () => this.reveal(5));
    this.later(4200, () => this.status.set('Reading — filed to Expenses / Nov 2026 / Groceries.'));
    this.later(4400, () => this.reveal(6));
    this.later(4800, () => this.finish());
  }

  /** Reveals field n in the extraction panel AND, the first time, its chip. The panel resets every loop; the chips never do. */
  private reveal(n: number): void {
    this.revealed.set(n);
    if (this.chipsRevealed() < n) {
      this.chipsRevealed.set(n);
    }
  }

  /** Mouse parallax on the 3D scene — skipped with reduced motion. */
  protected onTilt(event: MouseEvent): void {
    if (this.reducedMotion()) {
      return;
    }
    const el = this.scene()?.nativeElement;
    if (!el) {
      return;
    }
    const r = el.getBoundingClientRect();
    const px = (event.clientX - r.left) / r.width - 0.5;
    const py = (event.clientY - r.top) / r.height - 0.5;
    this.ry.set(Math.round(14 + px * 24));
    this.rx.set(Math.round(-10 - py * 18));
    this.px.set(Math.round(px * 100) / 100);
    this.py.set(Math.round(py * 100) / 100);
  }

  protected resetTilt(): void {
    this.rx.set(-10);
    this.ry.set(14);
    this.px.set(0);
    this.py.set(0);
  }

  /**
   * Ask iOS for motion permission from a tap on the scene (a user
   * gesture is required). No-op on desktop and on Android, where the
   * deviceorientation listener just works once added.
   */
  protected enableGyro(): void {
    const doe = (
      window as unknown as {
        DeviceOrientationEvent?: { requestPermission?: () => Promise<string> };
      }
    ).DeviceOrientationEvent;
    if (typeof doe?.requestPermission === 'function') {
      doe.requestPermission().catch(() => undefined);
    }
  }

  /** Phone gyro tilt — the mobile equivalent of onTilt. */
  private readonly onDeviceOrientation = (event: DeviceOrientationEvent): void => {
    if (this.reducedMotion() || event.gamma === null || event.beta === null) {
      return;
    }
    const gamma = Math.max(-30, Math.min(30, event.gamma));
    const beta = Math.max(25, Math.min(65, event.beta));
    this.ry.set(Math.round(14 + gamma * 0.5));
    this.rx.set(Math.round(-10 - (beta - 45) * 0.45));
    this.px.set(Math.round((gamma / 30) * 50) / 100);
    this.py.set(Math.round(((beta - 45) / 20) * 50) / 100);
  };

  /** Shoebox countdown: 9 → 0, one box every 150 ms, then it stays at 0. */
  private countShoeboxes(): void {
    this.shoeboxes.set(9);
    for (let i = 8; i >= 0; i--) {
      const value = i;
      this.shoeboxTimers.push(setTimeout(() => this.shoeboxes.set(value), (9 - i) * 150));
    }
  }

  protected onSearch(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
  }

  protected setFilter(value: string): void {
    this.filter.set(value);
  }

  private finish(): void {
    this.revealed.set(6);
    this.chipsRevealed.set(6);
    this.scanning.set(false);
    this.done.set(true);
    this.status.set('Done. Filed and searchable — try searching "Tesco" in the dashboard below.');
    // The demo loops on its own — a visitor never has to click "Scan
    // again". Hold the filed state a few seconds, then replay forever.
    // Reduced motion shows the filed state once and stops. A manual
    // replay (the button) clears the pending loop timer via scan(), so
    // there is only ever one loop chain running.
    if (!this.reducedMotion()) {
      this.later(4500, () => this.scan());
    }
  }

  private later(ms: number, fn: () => void): void {
    this.timers.push(setTimeout(fn, ms));
  }

  private startShowcase(): void {
    if (this.reducedMotion()) {
      this.showcasePhase.set(4);
      this.showcaseRunning.set(false);
      return;
    }
    if (this.showcaseRunning()) {
      return;
    }
    this.showcaseRunning.set(true);
    this.showcaseStarted = true;
    this.advanceShowcase();
  }

  private advanceShowcase(): void {
    this.showcaseTimers.push(
      setTimeout(() => {
        if (!this.showcaseRunning()) {
          return;
        }
        // One step per 2.54s: the five captions take exactly 12.68s, the
        // length of the showcase video, so the words stay in step with
        // the pictures instead of drifting apart.
        this.showcasePhase.set((this.showcasePhase() + 1) % 5);
        this.advanceShowcase();
      }, 2540),
    );
  }

  private reducedMotion(): boolean {
    return (
      typeof window !== 'undefined' &&
      window.matchMedia('(prefers-reduced-motion: reduce)').matches
    );
  }
}
