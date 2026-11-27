import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { Terms } from './terms';

// Phase 10 Terms of Service: a public, static page. The tests pin the
// content that matters — the page is really the terms, it says documents
// stay on the operator's own server and are never sent to an outside AI
// service, it carries no invented contact details, and it links home and
// to the Privacy Policy.
describe('Terms', () => {
  const create = async (): Promise<HTMLElement> => {
    await TestBed.configureTestingModule({
      imports: [Terms],
      providers: [provideRouter([])],
    }).compileComponents();
    const fixture = TestBed.createComponent(Terms);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  it('renders the Terms of Service heading and the last-updated date', async () => {
    const el = await create();
    expect(el.querySelector('h1')?.textContent).toBe('Terms of Service');
    expect(el.textContent).toContain('Last updated: 27 November 2026');
  });

  it('states the product accurately: on-server OCR, no outside AI service', async () => {
    const el = await create();
    expect(el.textContent).toContain('not sent to an outside AI service');
    expect(el.textContent).toContain('not tax advice');
    expect(el.textContent).toContain('We do not sell your documents');
  });

  it('does not invent contact details — the contact is a placeholder for the owner', async () => {
    const el = await create();
    expect(el.textContent).toContain('contact details to be added by the site owner');
    expect(el.textContent).not.toContain('@');
  });

  it('links back home and to the Privacy Policy', async () => {
    const el = await create();
    const links = [...el.querySelectorAll('a')].map((a) => a.getAttribute('href'));
    expect(links).toContain('/');
    expect(links).toContain('/privacy');
  });
});
