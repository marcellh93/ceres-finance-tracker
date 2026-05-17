import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { BudgetsLayout } from './BudgetsLayout';

let mockFetch: ReturnType<typeof vi.fn>;
beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string) => {
    if (url.startsWith('/api/category-budgets')) {
      return Promise.resolve({ ok: true, status: 200, json: async () => [] });
    }
    if (url.startsWith('/api/goal-budgets')) {
      return Promise.resolve({ ok: true, status: 200, json: async () => [] });
    }
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

vi.mock('../../lib/use-settings', () => ({
  useSettings: () => ({ data: { numberFormat: 'period_decimal', dateFormat: 'DD/MM/YYYY' }, loading: false }),
}));

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/budgets" element={<BudgetsLayout />}>
          <Route path="new" element={<div data-testid="create-page">CREATE</div>} />
          <Route path=":id/edit" element={<div data-testid="edit-page">EDIT</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('BudgetsLayout', () => {
  it('renders heading and tabs', async () => {
    renderAt('/budgets');
    expect(await screen.findByRole('heading', { name: /budgets/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /category budgets/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /goal budgets/i })).toBeInTheDocument();
  });

  it('shows the +New dropdown items on click', async () => {
    renderAt('/budgets');
    fireEvent.click(await screen.findByRole('button', { name: /^new$/i }));
    await waitFor(() => {
      expect(screen.getByText('Category Budget')).toBeInTheDocument();
      expect(screen.getByText('Spending Goal')).toBeInTheDocument();
      expect(screen.getByText('Savings Goal')).toBeInTheDocument();
    });
  });

  it('renders the empty-state when category list is empty', async () => {
    renderAt('/budgets?type=category');
    await waitFor(() => expect(screen.getByText(/no category budgets/i)).toBeInTheDocument());
  });

  it('renders the empty-state when goal list is empty', async () => {
    renderAt('/budgets?type=goal');
    await waitFor(() => expect(screen.getByText(/no goal budgets/i)).toBeInTheDocument());
  });

  it('child route /new renders Outlet content instead of list', async () => {
    renderAt('/budgets/new');
    await waitFor(() => expect(screen.getByTestId('create-page')).toBeInTheDocument());
  });
});
