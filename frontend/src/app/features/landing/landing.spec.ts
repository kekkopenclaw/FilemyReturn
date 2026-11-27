import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { Landing } from './landing';

// The scan demo's logic: fields appear in order, the receipt is filed,
// then the demo LOOPS on its own (a visitor never clicks "Scan again").
// Reduced motion shows the filed state once and never loops. The 3D tilt
// (mouse + phone gyro) moves the receipt and the parallax layers.
describe('Landing', () => {
  // The component's signals and scan/tilt helpers are protected — the
  // spec reaches them through this narrow cast, the same way a template
  // binding does.
  type LandingProbe = {
    scanning: () => boolean;
    done: () => boolean;
    revealed: () => number;
    status: () => string;
    rx: () => number;
    ry: () => number;
    px: () => number;
    py: () => number;
    scan: () => void;
    onTilt: (event: MouseEvent) => void;
    onDeviceOrientation: (event: {
      gamma: number | null;
      beta: number | null;
    }) => void;
  };

  const mockMatchMedia = (matches: boolean): void => {
    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      value: (query: string) => ({
        matches,
        media: query,
        onchange: null,
        addListener: () => undefined,
        removeListener: () => undefined,
        addEventListener: () => undefined,
        removeEventListener: () => undefined,
        dispatchEvent: () => false,
      }),
    });
  };

  const mockIntersectionObserver = (): void => {
    class IntersectionObserverMock {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    }
    vi.stubGlobal('IntersectionObserver', IntersectionObserverMock);
  };

  const create = async (): Promise<LandingProbe> => {
    await TestBed.configureTestingModule({
      imports: [Landing],
      providers: [provideRouter([])],
    }).compileComponents();
    const fixture = TestBed.createComponent(Landing);
    fixture.detectChanges();
    return fixture.componentInstance as unknown as LandingProbe;
  };

  beforeEach(() => {
    vi.useFakeTimers();
    mockMatchMedia(false);
    mockIntersectionObserver();
  });

  it('reveals the six fields in order and finishes filed', () => {
    return create().then((landing) => {
      landing.scan();
      expect(landing.scanning()).toBe(true);
      expect(landing.status()).toBe('Reading receipt…');

      vi.advanceTimersByTime(1400);
      expect(landing.revealed()).toBe(1);
      vi.advanceTimersByTime(600);
      expect(landing.revealed()).toBe(2);
      vi.advanceTimersByTime(600);
      expect(landing.revealed()).toBe(3);
      vi.advanceTimersByTime(600);
      expect(landing.revealed()).toBe(4);
      vi.advanceTimersByTime(600);
      expect(landing.revealed()).toBe(5);
      vi.advanceTimersByTime(600);
      expect(landing.revealed()).toBe(6);
      vi.advanceTimersByTime(400);
      expect(landing.scanning()).toBe(false);
      expect(landing.done()).toBe(true);
      expect(landing.status()).toContain('Done. Filed and searchable');
    });
  });

  it('loops on its own: the filed state replays after a few seconds', () => {
    return create().then((landing) => {
      landing.scan();
      vi.advanceTimersByTime(4800);
      expect(landing.done()).toBe(true);

      // Still filed just before the loop point.
      vi.advanceTimersByTime(4499);
      expect(landing.done()).toBe(true);
      expect(landing.scanning()).toBe(false);

      // One millisecond later the loop restarts the scan.
      vi.advanceTimersByTime(1);
      expect(landing.scanning()).toBe(true);
      expect(landing.done()).toBe(false);
      expect(landing.revealed()).toBe(0);
      expect(landing.status()).toBe('Reading receipt…');

      // And the replay finishes and loops again — forever.
      vi.advanceTimersByTime(4800);
      expect(landing.done()).toBe(true);
      vi.advanceTimersByTime(4500);
      expect(landing.scanning()).toBe(true);
    });
  });

  it('with reduced motion shows the filed state once and never loops', () => {
    mockMatchMedia(true);
    return create().then((landing) => {
      landing.scan();
      expect(landing.done()).toBe(true);
      expect(landing.revealed()).toBe(6);
      expect(landing.scanning()).toBe(false);

      vi.advanceTimersByTime(60_000);
      expect(landing.done()).toBe(true);
      expect(landing.scanning()).toBe(false);
    });
  });

  it('mouse tilt moves the receipt and the parallax layers', () => {
    return create().then((landing) => {
      const scene = document.querySelector('.scene') as HTMLElement;
      scene.getBoundingClientRect = () =>
        ({
          left: 0,
          top: 0,
          width: 400,
          height: 500,
          right: 400,
          bottom: 500,
          x: 0,
          y: 0,
          toJSON: () => ({}),
        }) as DOMRect;
      // Pointer at the top-right corner of the scene.
      landing.onTilt(new MouseEvent('mousemove', { clientX: 400, clientY: 0 }));
      expect(landing.ry()).toBe(18);
      expect(landing.rx()).toBe(-2);
      expect(landing.px()).toBe(0.5);
      expect(landing.py()).toBe(-0.5);
    });
  });

  it('phone gyro tilt moves the receipt the same way as the mouse', () => {
    return create().then((landing) => {
      landing.onDeviceOrientation({ gamma: 30, beta: 65 });
      expect(landing.ry()).toBe(22);
      expect(landing.rx()).toBe(-15);
      expect(landing.px()).toBe(0.5);
      expect(landing.py()).toBe(0.5);
      // A null reading is ignored — the tilt stays where it was.
      landing.onDeviceOrientation({ gamma: null, beta: null });
      expect(landing.ry()).toBe(22);
    });
  });
});
