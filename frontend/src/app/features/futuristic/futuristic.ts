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
 * Futuristic 3D concept page (/concept) — a separate page, NOT the landing.
 * Rebuilt in Angular on the cream theme (never dark): a paper receipt floats
 * in a light 3D scene, a teal scan beam sweeps it, extracted-data chips fly
 * out in sync, and a dashboard glimpse flattens on scroll with a working
 * search over sample documents. Pure CSS/JS 3D, no libraries.
 *
 * The receipt is the real one from the live OCR test: The Corner Cafe,
 * 24 November 2026, £18.40, confidence 95.7%.
 */
@Component({
  imports: [RouterLink],
  selector: 'app-futuristic',
  styleUrl: './futuristic.css',
  templateUrl: './futuristic.html',
})
export class Futuristic implements AfterViewInit, OnDestroy {
  protected readonly scanning = signal(false);
  protected readonly done = signal(false);
  protected readonly revealed = signal(0);
  protected readonly status = signal(
    'Press "Watch it scan" — the software reads the receipt live.',
  );
  protected readonly rx = signal(-8);
  protected readonly ry = signal(10);
  protected readonly dashboardFlat = signal(false);
  protected readonly query = signal('');

  protected readonly docs = [
    { name: 'Tesco receipt', kind: 'Groceries', date: '24 Nov 2026', amount: '£14.85', state: 'Ready' },
    { name: 'The Corner Cafe receipt', kind: 'Eating out', date: '24 Nov 2026', amount: '£18.40', state: 'Ready' },
    { name: 'Plumber invoice', kind: 'Home', date: '18 Nov 2026', amount: '£86.00', state: 'Ready' },
    { name: 'Electricity bill', kind: 'Utilities', date: '12 Nov 2026', amount: '£74.20', state: 'Awaiting OCR' },
    { name: 'HMRC letter', kind: 'Tax', date: '03 Nov 2026', amount: '—', state: 'Processing' },
  ];

  protected readonly filteredDocs = computed(() => {
    const q = this.query().trim().toLowerCase();
    if (!q) {
      return this.docs;
    }
    return this.docs.filter((d) =>
      `${d.name} ${d.kind} ${d.date} ${d.amount} ${d.state}`
        .toLowerCase()
        .includes(q),
    );
  });

  private readonly scene = viewChild<ElementRef<HTMLElement>>('scene');
  private readonly demo = viewChild<ElementRef<HTMLElement>>('demo');
  private readonly glimpse = viewChild<ElementRef<HTMLElement>>('glimpse');
  private observers: IntersectionObserver[] = [];
  private timers: ReturnType<typeof setTimeout>[] = [];
  private autoPlayed = false;

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
  }

  ngOnDestroy(): void {
    this.observers.forEach((o) => o.disconnect());
    this.timers.forEach((t) => clearTimeout(t));
  }

  /** Hero button: scroll to the scene and run the scan. */
  protected watchScan(): void {
    const el = this.demo()?.nativeElement;
    el?.scrollIntoView({
      behavior: this.reducedMotion() ? 'auto' : 'smooth',
      block: 'start',
    });
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
    this.status.set('Scanning — reading the receipt…');

    if (this.reducedMotion()) {
      this.finish();
      return;
    }

    this.later(1200, () => this.status.set('Reading — shop found.'));
    this.later(1400, () => this.revealed.set(1));
    this.later(1800, () => this.revealed.set(2));
    this.later(2200, () => this.revealed.set(3));
    this.later(2600, () => this.revealed.set(4));
    this.later(3000, () => this.finish());
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
    this.ry.set(Math.round(10 + px * 16));
    this.rx.set(Math.round(-8 - py * 12));
  }

  protected resetTilt(): void {
    this.rx.set(-8);
    this.ry.set(10);
  }

  protected onSearch(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
  }

  private finish(): void {
    this.revealed.set(4);
    this.scanning.set(false);
    this.done.set(true);
    this.status.set('Done — filed as Eating out, ready to search.');
  }

  private later(ms: number, fn: () => void): void {
    this.timers.push(setTimeout(fn, ms));
  }

  private reducedMotion(): boolean {
    return (
      typeof window !== 'undefined' &&
      window.matchMedia('(prefers-reduced-motion: reduce)').matches
    );
  }
}
