import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BudgetRowMenu } from './BudgetRowMenu';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));
import { toast } from 'sonner';

let mockFetch: ReturnType<typeof vi.fn>;

beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
});

function renderMenu({
  budgetId = 'b1',
  kind = 'CategoryBudget' as const,
  isActive = true,
  noun = 'category budget',
  onChanged = vi.fn(),
} = {}) {
  render(
    <MemoryRouter initialEntries={['/budgets']}>
      <Routes>
        <Route
          path="/budgets"
          element={
            <BudgetRowMenu
              budgetId={budgetId}
              kind={kind}
              isActive={isActive}
              noun={noun}
              onChanged={onChanged}
            />
          }
        />
        <Route path="/budgets/:id/edit" element={<div data-testid="edit-page">EDIT</div>} />
      </Routes>
    </MemoryRouter>,
  );
  return { onChanged };
}

describe('BudgetRowMenu', () => {
  it('opens the menu and shows Edit + Archive when active', async () => {
    renderMenu({ isActive: true });
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    await waitFor(() => {
      expect(screen.getByText('Edit')).toBeInTheDocument();
      expect(screen.getByText('Archive')).toBeInTheDocument();
    });
  });

  it('opens the menu and shows Edit + Reactivate when archived', async () => {
    renderMenu({ isActive: false });
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    await waitFor(() => {
      expect(screen.getByText('Reactivate')).toBeInTheDocument();
    });
  });

  it('Edit navigates to /budgets/:id/edit', async () => {
    renderMenu();
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Edit'));
    await waitFor(() => expect(screen.getByTestId('edit-page')).toBeInTheDocument());
  });

  it('Archive: confirm → PATCHes archive → toasts → onChanged', async () => {
    mockFetch.mockResolvedValueOnce({ status: 204, ok: true, json: async () => null });
    const { onChanged } = renderMenu();
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Archive'));
    // AlertDialog confirm
    fireEvent.click(await screen.findByRole('button', { name: /^archive$/i }));
    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalledWith(
        '/api/category-budgets/b1/archive',
        expect.objectContaining({ method: 'PATCH' }),
      );
      expect(toast.success).toHaveBeenCalledWith('Category budget archived.');
      expect(onChanged).toHaveBeenCalled();
    });
  });

  it('Reactivate: single click → PATCHes reactivate → toasts', async () => {
    mockFetch.mockResolvedValueOnce({ status: 204, ok: true, json: async () => null });
    renderMenu({ isActive: false });
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Reactivate'));
    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalledWith(
        '/api/category-budgets/b1/reactivate',
        expect.objectContaining({ method: 'PATCH' }),
      );
      expect(toast.success).toHaveBeenCalledWith('Category budget reactivated.');
    });
  });

  it('Reactivate 409 surfaces the error message', async () => {
    mockFetch.mockResolvedValueOnce({
      status: 409,
      ok: false,
      json: async () => ({
        error: {
          code: 'DUPLICATE_BUDGET',
          message: 'A budget for this category and currency already exists.',
        },
      }),
    });
    renderMenu({ isActive: false });
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Reactivate'));
    await waitFor(() => {
      expect(toast.error).toHaveBeenCalledWith(
        'A budget for this category and currency already exists.',
      );
    });
  });
});
