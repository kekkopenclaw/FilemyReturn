import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { of, throwError } from 'rxjs';
import { ApiService } from '../../core/api.service';
import {
  Archive,
  FilingRule,
  Tag,
  Workspace,
} from '../../core/models';
import { WorkspaceService } from '../../core/workspace.service';
import { FilingRules } from './filing-rules';

// Phase 4 organisation: filing rules file documents automatically — vendor,
// category or file-name conditions, and assign-a-tag / set-a-category /
// move-to-an-archive actions. The form must refuse a rule with no condition
// or no action, and create / edit / toggle / delete all go through the API.
describe('FilingRules', () => {
  const workspace: Workspace = {
    id: 'ws-1',
    name: 'Test WS',
    description: null,
    role: 'Owner',
    createdAt: '2026-11-27T00:00:00Z',
    createdById: 'u1',
  };

  const tags: Tag[] = [
    {
      id: 't1',
      workspaceId: 'ws-1',
      name: 'Expenses',
      colour: '#0e7c66',
      createdAt: '2026-11-27T00:00:00Z',
    },
  ];

  const archives: Archive[] = [
    {
      id: 'a1',
      workspaceId: 'ws-1',
      name: 'Filed 2026',
      description: null,
      createdAt: '2026-11-27T00:00:00Z',
    },
  ];

  const rule: FilingRule = {
    id: 'r1',
    workspaceId: 'ws-1',
    name: 'Tesco expenses',
    isEnabled: true,
    priority: 0,
    vendorContains: 'Tesco',
    category: null,
    fileNameContains: null,
    assignTagId: 't1',
    setCategory: 'Receipt',
    moveToArchiveId: null,
    appliedCount: 3,
    createdAt: '2026-11-27T00:00:00Z',
  };

  let api: {
    getFilingRules: ReturnType<typeof vi.fn>;
    getTags: ReturnType<typeof vi.fn>;
    getArchives: ReturnType<typeof vi.fn>;
    createFilingRule: ReturnType<typeof vi.fn>;
    updateFilingRule: ReturnType<typeof vi.fn>;
    deleteFilingRule: ReturnType<typeof vi.fn>;
  };

  const create = async (rules: FilingRule[] = [rule]) => {
    api = {
      getFilingRules: vi.fn(() => of(rules)),
      getTags: vi.fn(() => of(tags)),
      getArchives: vi.fn(() => of(archives)),
      createFilingRule: vi.fn(
        (_: string, input: Record<string, unknown>) =>
          of({ ...rule, id: 'r-new', ...input }),
      ),
      updateFilingRule: vi.fn(
        (_: string, __: string, input: Record<string, unknown>) =>
          of({ ...rule, ...input }),
      ),
      deleteFilingRule: vi.fn(() => of(undefined)),
    };
    const workspaceService = {
      workspaces: signal([workspace]),
      loading: signal(false),
      selectedWorkspaceId: signal('ws-1'),
      selectedWorkspace: signal(workspace),
    };
    await TestBed.configureTestingModule({
      imports: [FilingRules],
      providers: [
        { provide: ApiService, useValue: api },
        { provide: WorkspaceService, useValue: workspaceService },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(FilingRules);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    return { fixture, el };
  };

  const setInput = (el: HTMLElement, selector: string, value: string) => {
    const input = el.querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };

  const setSelect = (el: HTMLElement, selector: string, value: string) => {
    const select = el.querySelector(selector) as HTMLSelectElement;
    select.value = value;
    select.dispatchEvent(new Event('change'));
  };

  const submitForm = (el: HTMLElement) => {
    const form = el.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
  };

  it('renders a rule with its conditions, actions and applied count', async () => {
    const { el } = await create();
    expect(el.textContent).toContain('Tesco expenses');
    expect(el.textContent).toContain('vendor contains “Tesco”');
    expect(el.textContent).toContain('tag “Expenses”');
    expect(el.textContent).toContain('category → Receipt');
    expect(el.textContent).toContain('Applied 3 times');
  });

  it('refuses a rule with no condition', async () => {
    const { fixture, el } = await create([]);
    setInput(el, '#rule-name', 'No condition');
    setSelect(el, '#rule-assign-tag', 't1');
    submitForm(el);
    fixture.detectChanges();
    expect(api.createFilingRule).not.toHaveBeenCalled();
    expect(el.textContent).toContain('at least one condition');
  });

  it('refuses a rule with no action', async () => {
    const { fixture, el } = await create([]);
    setInput(el, '#rule-name', 'No action');
    setInput(el, '#rule-vendor', 'Tesco');
    submitForm(el);
    fixture.detectChanges();
    expect(api.createFilingRule).not.toHaveBeenCalled();
    expect(el.textContent).toContain('at least one action');
  });

  it('creating a rule calls the API and appends it to the list', async () => {
    const { fixture, el } = await create([]);
    setInput(el, '#rule-name', 'Tesco expenses');
    setInput(el, '#rule-vendor', 'Tesco');
    setSelect(el, '#rule-assign-tag', 't1');
    setSelect(el, '#rule-set-category', 'Receipt');
    submitForm(el);
    fixture.detectChanges();
    expect(api.createFilingRule).toHaveBeenCalledWith('ws-1', {
      name: 'Tesco expenses',
      isEnabled: true,
      priority: 0,
      vendorContains: 'Tesco',
      category: null,
      fileNameContains: null,
      assignTagId: 't1',
      setCategory: 'Receipt',
      moveToArchiveId: null,
    });
    expect(el.textContent).toContain('Rule created.');
    expect(el.textContent).toContain('Tesco expenses');
  });

  it('a duplicate rule name shows an error and adds nothing', async () => {
    const { fixture, el } = await create([]);
    api.createFilingRule.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({ status: 400, statusText: 'Bad Request' }),
      ),
    );
    setInput(el, '#rule-name', 'Tesco expenses');
    setInput(el, '#rule-vendor', 'Tesco');
    setSelect(el, '#rule-assign-tag', 't1');
    submitForm(el);
    fixture.detectChanges();
    expect(el.textContent).toContain('unique');
    expect(el.querySelectorAll('.rule-row').length).toBe(0);
  });

  it('toggling a rule calls update with the flipped flag', async () => {
    const { fixture, el } = await create();
    const toggle = Array.from(el.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === 'Disable rule Tesco expenses',
    ) as HTMLButtonElement;
    toggle.click();
    fixture.detectChanges();
    expect(api.updateFilingRule).toHaveBeenCalledWith('ws-1', 'r1', {
      name: 'Tesco expenses',
      isEnabled: false,
      priority: 0,
      vendorContains: 'Tesco',
      category: null,
      fileNameContains: null,
      assignTagId: 't1',
      setCategory: 'Receipt',
      moveToArchiveId: null,
    });
  });

  it('editing loads the rule into the form and saving calls update', async () => {
    const { fixture, el } = await create();
    const edit = Array.from(el.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === 'Edit rule Tesco expenses',
    ) as HTMLButtonElement;
    edit.click();
    fixture.detectChanges();
    expect(
      (el.querySelector('#rule-name') as HTMLInputElement).value,
    ).toBe('Tesco expenses');
    expect(
      (el.querySelector('#rule-vendor') as HTMLInputElement).value,
    ).toBe('Tesco');
    setInput(el, '#rule-vendor', 'Sainsbury');
    submitForm(el);
    fixture.detectChanges();
    expect(api.updateFilingRule).toHaveBeenCalledWith('ws-1', 'r1', {
      name: 'Tesco expenses',
      isEnabled: true,
      priority: 0,
      vendorContains: 'Sainsbury',
      category: null,
      fileNameContains: null,
      assignTagId: 't1',
      setCategory: 'Receipt',
      moveToArchiveId: null,
    });
    expect(el.textContent).toContain('Rule updated.');
  });

  it('deleting a rule calls the API and removes it from the list', async () => {
    const { fixture, el } = await create();
    const remove = Array.from(el.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === 'Delete rule Tesco expenses',
    ) as HTMLButtonElement;
    remove.click();
    fixture.detectChanges();
    expect(api.deleteFilingRule).toHaveBeenCalledWith('ws-1', 'r1');
    expect(el.textContent).not.toContain('Tesco expenses');
  });
});
