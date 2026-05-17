import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { toast } from 'sonner';
import { CategoryRowMenu } from './CategoryRowMenu';
import type { CategoryListItemDto } from './categories-api';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

let mockFetch: ReturnType<typeof vi.fn>;

const activeRow: CategoryListItemDto = {
  id: 'a-1',
  name: 'Groceries',
  categoryTypeId: 2,
  categoryTypeName: 'Expense',
  lifestyleTag: 'Needs',
  isActive: true,
  isSystem: false,
};

const archivedRow: CategoryListItemDto = { ...activeRow, id: 'a-2', name: 'Old', isActive: false };

beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
});

afterEach(() => vi.resetAllMocks());

function renderMenu(category: CategoryListItemDto, onChanged = vi.fn()) {
  return render(
    <MemoryRouter initialEntries={['/categories']}>
      <Routes>
        <Route path="/categories" element={<CategoryRowMenu category={category} onChanged={onChanged} />} />
        <Route path="/categories/:id/edit" element={<div data-testid="edit-page">EDIT</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('CategoryRowMenu', () => {
  it('shows Edit and Archive items for active user rows', async () => {
    renderMenu(activeRow);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    expect(await screen.findByText('Edit')).toBeInTheDocument();
    expect(screen.getByText('Archive…')).toBeInTheDocument();
  });

  it('shows Edit + Reactivate for archived rows (no Archive)', async () => {
    renderMenu(archivedRow);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    expect(await screen.findByText('Edit')).toBeInTheDocument();
    expect(screen.getByText('Reactivate')).toBeInTheDocument();
    expect(screen.queryByText('Archive…')).toBeNull();
  });

  it('reactivate 204 fires toast.success and onChanged', async () => {
    mockFetch.mockResolvedValue({ ok: true, status: 204, json: async () => null });
    const onChanged = vi.fn();
    renderMenu(archivedRow, onChanged);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Reactivate'));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Reactivated.'));
    expect(onChanged).toHaveBeenCalledTimes(1);
  });

  it('clicking Edit navigates to /categories/:id/edit', async () => {
    renderMenu(activeRow);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Edit'));
    expect(await screen.findByTestId('edit-page')).toBeInTheDocument();
  });

  it('clicking Archive opens the confirm dialog with the category name', async () => {
    renderMenu(activeRow);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Archive…'));
    expect(await screen.findByText(/archive 'groceries'\?/i)).toBeInTheDocument();
  });

  it('archive 204 fires toast.success and onChanged', async () => {
    mockFetch.mockResolvedValue({ ok: true, status: 204, json: async () => null });
    const onChanged = vi.fn();
    renderMenu(activeRow, onChanged);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Archive…'));
    fireEvent.click(await screen.findByRole('button', { name: 'Archive' }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Archived.'));
    expect(onChanged).toHaveBeenCalledTimes(1);
  });

  it('archive 409 fires toast.error with the in-use message', async () => {
    mockFetch.mockResolvedValue({
      ok: false,
      status: 409,
      json: async () => ({ error: { code: 'CATEGORY_IN_USE', message: 'This category has transactions. Reassign them before archiving.', details: [] } }),
    });
    renderMenu(activeRow);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Archive…'));
    fireEvent.click(await screen.findByRole('button', { name: 'Archive' }));
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith('This category has transactions. Reassign them before archiving.'),
    );
  });

  it('archive other-error fires the generic toast', async () => {
    mockFetch.mockResolvedValue({ ok: false, status: 500, json: async () => null });
    renderMenu(activeRow);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Archive…'));
    fireEvent.click(await screen.findByRole('button', { name: 'Archive' }));
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Couldn't archive. Try again."),
    );
  });
});
