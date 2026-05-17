import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';
import { toast } from 'sonner';
import { CategoryCreate } from './CategoryCreate';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

let mockFetch: ReturnType<typeof vi.fn>;

const categoryTypesResponse = [
  { id: 1, name: 'Income' },
  { id: 2, name: 'Expense' },
];

beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string, init?: RequestInit) => {
    if (url === '/api/category-types') {
      return Promise.resolve({ ok: true, status: 200, json: async () => categoryTypesResponse });
    }
    if (url === '/api/categories' && init?.method === 'POST') {
      return Promise.resolve({ ok: true, status: 201, json: async () => ({ id: 'new-1' }) });
    }
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

afterEach(() => vi.resetAllMocks());

function LayoutShim() {
  return <Outlet context={{ refetch: vi.fn() }} />;
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/categories/new']}>
      <Routes>
        <Route path="/categories" element={<LayoutShim />}>
          <Route index element={<div data-testid="list-page">LIST</div>} />
          <Route path="new" element={<CategoryCreate />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('CategoryCreate', () => {
  it('renders the form when category-types load', async () => {
    renderPage();
    await screen.findByLabelText(/name/i);
    expect(screen.getByLabelText(/type/i)).toBeInTheDocument();
  });

  it('POST 2xx fires toast.success and navigates back', async () => {
    renderPage();
    const nameInput = await screen.findByLabelText(/name/i);
    fireEvent.change(nameInput, { target: { value: 'Coffee' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Created.'));
    expect(await screen.findByTestId('list-page')).toBeInTheDocument();
  });

  it('POST 422 fires toast.error and form retains values', async () => {
    mockFetch.mockImplementation((url: string, init?: RequestInit) => {
      if (url === '/api/category-types') {
        return Promise.resolve({ ok: true, status: 200, json: async () => categoryTypesResponse });
      }
      if (url === '/api/categories' && init?.method === 'POST') {
        return Promise.resolve({ ok: false, status: 422, json: async () => ({ error: { code: 'VALIDATION_ERROR', message: 'Bad', details: [] } }) });
      }
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderPage();
    const nameInput = await screen.findByLabelText(/name/i);
    fireEvent.change(nameInput, { target: { value: 'Coffee' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Couldn't save. Try again."),
    );
    expect(screen.getByLabelText(/name/i)).toHaveValue('Coffee');
  });
});
