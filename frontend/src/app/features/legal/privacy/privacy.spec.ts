import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { Privacy } from './privacy';

// Phase 10 Privacy Policy: a public, static page. The tests pin the
// content that matters — the page is really the policy, it says what is
// collected and that nothing is sold or sent to an outside AI service, it
// carries no invented contact details, and it links home and to the
// Terms of Service.
describe('Privacy', () => {
  const create = async (): Promise<HTMLElement> => {
    await TestBed.configureTestingModule({
      imports: [Privacy],
      providers: [provideRouter([])],
    }).compileComponents();
    const fixture = TestBed.createComponent(Privacy);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  it('renders the Privacy Policy heading and the last-updated date', async () => {
    const el = await create();
    expect(el.querySelector('h1')?.textContent).toBe('Privacy Policy');
    expect(el.textContent).toContain('Last updated: 27 November 2026');
  });

  it('states what is collected and that nothing is sold or sent outside', async () => {
    const el = await create();
    expect(el.textContent).toContain('Account details');
    expect(el.textContent).toContain('Your documents');
    expect(el.textContent).toContain('We do not sell your personal data');
    expect(el.textContent).toContain('never sent to an outside AI service');
    expect(el.textContent).toContain('no tracking');
  });

  it('does not invent contact details — the contact is a placeholder for the owner', async () => {
    const el = await create();
    expect(el.textContent).toContain('contact details to be added by the site owner');
    expect(el.textContent).not.toContain('@');
  });

  it('links back home and to the Terms of Service', async () => {
    const el = await create();
    const links = [...el.querySelectorAll('a')].map((a) => a.getAttribute('href'));
    expect(links).toContain('/');
    expect(links).toContain('/terms');
  });
});
