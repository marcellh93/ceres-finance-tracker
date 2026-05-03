import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { CategoriesLayout } from './CategoriesLayout';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

const mockFetch = vi.fn();

const SYSTEM_ID = '20000000-0000-0000-0000-000000000001';
const UNCAT_INCOME_ID = '20000000-0000-0000-0000-000000000025';

const allRows = [
  // Income
  { id: 'i-1', name: 'Salary',    categoryTypeId: 1, categoryTypeName: 'Income',  lifestyleTag: null,    isActive: true,  isSystem: false },
  { id: 'i-2', name: 'Freelance', categoryTypeId: 1, categoryTypeName: 'Income',  lifestyleTag: null,    isActive: true,  isSystem: false },
  { id: SYSTEM_ID,                name: 'Opening Balance',     categoryTypeId: 1, categoryTypeName: 'Income',  lifestyleTag: null, isActive: true, isSystem: true },
  { id: UNCAT_INCOME_ID,          name: 'Uncategorized Income', categoryTypeId: 1, categoryTypeName: 'Income',  lifestyleTag: null, isActive: true, isSystem: false },
  // Expense
  { id: 'e-1', name: 'Groceries', categoryTypeId: 2, categoryTypeName: 'Expense', lifestyleTag: 'Needs', isActive: true,  isSystem: false },
  { id: 'e-2', name: 'Coffee',    categoryTypeId: 2, categoryTypeName: 'Expense', lifestyleTag: 'Wants', isActive: true,  isSystem: false },
  { id: 'e-3', name: 'Old',       categoryTypeId: 2, categoryTypeName: 'Expense', lifestyleTag: null,    isActive: false, isSystem: false },
];

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string) => {
    if (url === '/api/categories') {
      return Promise.resolve({
        ok: true,
        status: 200,
        json: async () => allRows.filter((r) => r.isActive),
      });
    }
    if (url === '/api/categories?includeInactive=true') {
      return Promise.resolve({ ok: true, status: 200, json: async () => allRows });
    }
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

afterEach(() => vi.resetAllMocks());

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/categories" element={<CategoriesLayout />}>
          <Route path="new" element={<div data-testid="new-page">NEW</div>} />
          <Route path=":id/edit" element={<div data-testid="edit-page">EDIT</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('CategoriesLayout', () => {
  it('renders skeleton while loading', () => {
    mockFetch.mockImplementation(() => new Promise(() => {}));
    renderAt('/categories');
    expect(screen.getByTestId('categories-skeleton')).toBeInTheDocument();
  });

  it('defaults to the Expense tab', async () => {
    renderAt('/categories');
    await screen.findByText('Groceries');
    expect(screen.queryByText('Salary')).toBeNull();
  });

  it('switching to Income tab updates the URL and shows Income rows', async () => {
    renderAt('/categories');
    await screen.findByText('Groceries');
    fireEvent.click(screen.getByRole('tab', { name: /income/i }));
    await screen.findByText('Salary');
    expect(screen.queryByText('Groceries')).toBeNull();
  });

  it('search filters rows in the active tab', async () => {
    renderAt('/categories');
    await screen.findByText('Groceries');
    fireEvent.change(screen.getByPlaceholderText(/filter categories/i), {
      target: { value: 'cof' },
    });
    await waitFor(() => {
      expect(screen.queryByText('Groceries')).toBeNull();
      expect(screen.getByText('Coffee')).toBeInTheDocument();
    });
  });

  it('shows search-empty state with Clear search link when no rows match', async () => {
    renderAt('/categories');
    await screen.findByText('Groceries');
    fireEvent.change(screen.getByPlaceholderText(/filter categories/i), {
      target: { value: 'zzznomatch' },
    });
    await waitFor(() => {
      expect(screen.getByText(/no categories match/i)).toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: /clear search/i })).toBeInTheDocument();
  });

  it('Include archived toggle adds archived rows', async () => {
    renderAt('/categories');
    await screen.findByText('Groceries');
    expect(screen.queryByText('Old')).toBeNull();
    fireEvent.click(screen.getByRole('switch', { name: /include archived/i }));
    await screen.findByText('Old');
  });

  it('archived rows render the Archived badge and opacity treatment', async () => {
    renderAt('/categories?includeInactive=true');
    const oldRow = (await screen.findByText('Old')).closest('tr')!;
    expect(within(oldRow).getByText('Archived')).toBeInTheDocument();
    expect(oldRow.className).toContain('opacity-60');
  });

  it('system rows render the locked-system marker and no row-actions menu', async () => {
    renderAt('/categories?type=income');
    const systemRow = (await screen.findByText('Opening Balance')).closest('tr')!;
    expect(within(systemRow).getByRole('button', { name: /system category/i })).toBeInTheDocument();
    expect(within(systemRow).queryByRole('button', { name: /row actions/i })).toBeNull();
  });

  it('reserved Uncategorized rows are treated as system (locked marker + no menu)', async () => {
    renderAt('/categories?type=income');
    const uncatRow = (await screen.findByText('Uncategorized Income')).closest('tr')!;
    expect(within(uncatRow).getByRole('button', { name: /system category/i })).toBeInTheDocument();
    expect(within(uncatRow).queryByRole('button', { name: /row actions/i })).toBeNull();
  });

  it('system rows sort to the bottom of their tab', async () => {
    renderAt('/categories?type=income');
    await screen.findByText('Opening Balance');
    const allRowsRendered = screen.getAllByRole('row');
    const names = allRowsRendered.slice(1).map((r) => within(r).getAllByRole('cell')[0]?.textContent ?? '');
    const last = names[names.length - 1];
    const secondLast = names[names.length - 2];
    expect(last).toMatch(/Opening Balance|Uncategorized Income/);
    expect(secondLast).toMatch(/Opening Balance|Uncategorized Income/);
  });

  it('GET error renders CardError with Retry', async () => {
    mockFetch.mockImplementation(() =>
      Promise.resolve({ ok: false, status: 500, json: async () => null }),
    );
    renderAt('/categories');
    await waitFor(() =>
      expect(screen.getByText(/Categories/)).toBeInTheDocument(),
    );
    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
  });

  it('clicking New category navigates to /categories/new', async () => {
    renderAt('/categories');
    await screen.findByText('Groceries');
    fireEvent.click(screen.getByRole('button', { name: /new category/i }));
    expect(await screen.findByTestId('new-page')).toBeInTheDocument();
  });
});
