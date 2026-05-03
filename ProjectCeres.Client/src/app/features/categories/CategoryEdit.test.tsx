import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';
import { toast } from 'sonner';
import { CategoryEdit } from './CategoryEdit';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

const mockFetch = vi.fn();

const categoryDetail = {
  id: 'cat-1',
  name: 'Groceries',
  categoryTypeId: 2,
  categoryTypeName: 'Expense',
  lifestyleTag: 'Needs',
  isActive: true,
  isSystem: false,
};

const categoryTypesResponse = [
  { id: 1, name: 'Income' },
  { id: 2, name: 'Expense' },
];

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string, init?: RequestInit) => {
    if (url === '/api/category-types') {
      return Promise.resolve({ ok: true, status: 200, json: async () => categoryTypesResponse });
    }
    if (url === '/api/categories/cat-1' && (!init || init.method === undefined)) {
      return Promise.resolve({ ok: true, status: 200, json: async () => categoryDetail });
    }
    if (url === '/api/categories/cat-missing') {
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    }
    if (url === '/api/categories/cat-1' && init?.method === 'PATCH') {
      return Promise.resolve({ ok: true, status: 200, json: async () => categoryDetail });
    }
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

afterEach(() => vi.resetAllMocks());

function LayoutShim() {
  return <Outlet context={{ refetch: vi.fn() }} />;
}

function renderPage(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/categories" element={<LayoutShim />}>
          <Route index element={<div data-testid="list-page">LIST</div>} />
          <Route path=":id/edit" element={<CategoryEdit />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('CategoryEdit', () => {
  it('renders the form pre-populated with the category', async () => {
    renderPage('/categories/cat-1/edit');
    await waitFor(() => expect(screen.getByLabelText(/name/i)).toHaveValue('Groceries'));
  });

  it('GET 404 renders the not-found banner with a link back', async () => {
    renderPage('/categories/cat-missing/edit');
    await waitFor(() =>
      expect(screen.getByText(/that category doesn't exist/i)).toBeInTheDocument(),
    );
    expect(screen.getByRole('button', { name: /back to categories/i })).toBeInTheDocument();
  });

  it('PATCH success fires toast.success and navigates back', async () => {
    renderPage('/categories/cat-1/edit');
    await waitFor(() => expect(screen.getByLabelText(/name/i)).toHaveValue('Groceries'));
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Food' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Saved.'));
    expect(await screen.findByTestId('list-page')).toBeInTheDocument();
  });

  it('PATCH 422 fires toast.error and form retains values', async () => {
    mockFetch.mockImplementation((url: string, init?: RequestInit) => {
      if (url === '/api/category-types') {
        return Promise.resolve({ ok: true, status: 200, json: async () => categoryTypesResponse });
      }
      if (url === '/api/categories/cat-1' && (!init || init.method === undefined)) {
        return Promise.resolve({ ok: true, status: 200, json: async () => categoryDetail });
      }
      if (url === '/api/categories/cat-1' && init?.method === 'PATCH') {
        return Promise.resolve({ ok: false, status: 422, json: async () => ({ error: { code: 'VALIDATION_ERROR', message: 'Bad', details: [] } }) });
      }
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderPage('/categories/cat-1/edit');
    await waitFor(() => expect(screen.getByLabelText(/name/i)).toHaveValue('Groceries'));
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Food' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Couldn't save. Try again."),
    );
    expect(screen.getByLabelText(/name/i)).toHaveValue('Food');
  });
});
