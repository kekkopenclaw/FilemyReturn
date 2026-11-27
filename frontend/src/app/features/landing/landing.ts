import {
  AfterViewInit,
  Component,
  ElementRef,
  OnDestroy,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Public landing page (DocuArchive home page).
 *
 * Purely presentational: every button is a real link (log in, register, or
 * an on-page section), except the scan demo, which runs a client-side
 * animation over sample receipt data. A route guard keeps signed-in users
 * out — they go to /dashboard instead.
 *
 * The "watch me scan" demo auto-plays once when scrolled into view and
 * replays from its button. With reduced motion it skips the animation and
 * shows the extracted fields straight away.
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
  protected readonly status = signal(
    'Press "Scan this receipt" to watch DocuArchive read it.',
  );

  private readonly demo = viewChild<ElementRef<HTMLElement>>('demo');
  private observer?: IntersectionObserver;
  private timers: ReturnType<typeof setTimeout>[] = [];
  private autoPlayed = false;

  ngAfterViewInit(): void {
    const el = this.demo()?.nativeElement;
    if (!el || typeof IntersectionObserver === 'undefined') {
      return;
    }
    this.observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting) && !this.autoPlayed) {
          this.autoPlayed = true;
          this.scan();
        }
      },
      { threshold: 0.4 },
    );
    this.observer.observe(el);
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
    this.timers.forEach((timer) => clearTimeout(timer));
  }

  /** Hero button: scroll to the demo and run the scan. */
  protected watchScan(): void {
    const el = this.demo()?.nativeElement;
    const reduced =
      typeof window !== 'undefined' &&
      window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    el?.scrollIntoView({ behavior: reduced ? 'auto' : 'smooth', block: 'start' });
    this.scan();
  }

  protected scan(): void {
    if (this.scanning()) {
      return;
    }
    this.timers.forEach((timer) => clearTimeout(timer));
    this.timers = [];
    this.scanning.set(true);
    this.done.set(false);
    this.revealed.set(0);
    this.status.set('Scanning — reading the receipt…');

    if (
      typeof window !== 'undefined' &&
      window.matchMedia('(prefers-reduced-motion: reduce)').matches
    ) {
      this.finish();
      return;
    }

    // The scan line sweeps for 1.2s, then each extracted field appears
    // one by one, in the order the software finds it.
    this.later(1200, () => this.status.set('Reading — shop found.'));
    this.later(1400, () => this.revealed.set(1));
    this.later(1800, () => this.revealed.set(2));
    this.later(2200, () => this.revealed.set(3));
    this.later(2600, () => this.revealed.set(4));
    this.later(3000, () => this.finish());
  }

  private finish(): void {
    this.revealed.set(4);
    this.scanning.set(false);
    this.done.set(true);
    this.status.set('Done — filed as Groceries, ready to search.');
  }

  private later(ms: number, fn: () => void): void {
    this.timers.push(setTimeout(fn, ms));
  }
}
