import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Outlet, MemoryRouter, Route, Routes } from 'react-router-dom';
import { MovementEdit } from './MovementEdit';

// ── Mock sonner ──
vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));
import { toast } from 'sonner';

// ── Mock fetch ──
const mockFetch = vi.fn();

const TRANSACTION_DTO = {
  id: 'abc-123',
  date: '2025-03-15',
  amount: 250.0,
  accountId: 'a1',
  categoryId: 'c1',
  description: 'March salary',
  isCleared: true,
  attachments: [],
};

function defaultFetchImpl(url: string, init?: RequestInit) {
  const method = (init?.method ?? 'GET').toUpperCase();

  if (method === 'GET' && url === '/api/movements/abc-123') {
    return Promise.resolve({
      ok: true,
      status: 200,
      json: async () => ({ id: 'abc-123', movementType: 'Transaction' }),
    });
  }
  if (method === 'GET' && url === '/api/transactions/abc-123') {
    return Promise.resolve({
      ok: true,
      status: 200,
      json: async () => TRANSACTION_DTO,
    });
  }
  if (method === 'GET' && url === '/api/accounts/active') {
    return Promise.resolve({
      ok: true,
      status: 200,
      json: async () => [
        { id: 'a1', name: 'Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
      ],
    });
  }
  if (method === 'GET' && url === '/api/categories/active') {
    return Promise.resolve({
      ok: true,
      status: 200,
      json: async () => [
        { id: 'c1', name: 'Salary', categoryTypeName: 'Income' },
      ],
    });
  }
  if (method === 'PUT' && url === '/api/transactions/abc-123') {
    return Promise.resolve({ ok: true, status: 204, json: async () => null });
  }
  if (method === 'DELETE' && url === '/api/transactions/abc-123') {
    return Promise.resolve({ ok: true, status: 204, json: async () => null });
  }

  return Promise.resolve({ ok: false, status: 500, json: async () => ({}) });
}

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation(defaultFetchImpl);
});

afterEach(() => {
  vi.resetAllMocks();
});

// ── Route helpers ──

const refetchMock = vi.fn();

function OutletShim() {
  return <Outlet context={{ refetch: refetchMock }} />;
}

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/movements" element={<OutletShim />}>
          <Route path="new" element={<div data-testid="create-page">CREATE</div>} />
          <Route path=":id/edit" element={<MovementEdit />} />
          <Route index element={<div data-testid="list-page">LIST</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

// ── Tests ──

describe('MovementEdit', () => {
  it('Test 1: loads discriminator, typed entity, then renders form prefilled', async () => {
    renderAt('/movements/abc-123/edit');

    // Wait for form to appear
    await waitFor(() => {
      expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
    });

    // Description input should be prefilled from loaded DTO
    const descInput = screen.getByLabelText(/description/i) as HTMLInputElement;
    expect(descInput.value).toBe('March salary');
  });

  it('Test 2: Save → PUT called → success toast shown', async () => {
    renderAt('/movements/abc-123/edit');

    await waitFor(() => {
      expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
    });

    // Change description
    fireEvent.change(screen.getByLabelText(/description/i), {
      target: { value: 'Updated salary' },
    });

    // Click Save
    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() => {
      expect(toast.success).toHaveBeenCalledWith('Saved.');
    });

    // Navigates back to the list view after save
    await waitFor(() => {
      expect(screen.getByTestId('list-page')).toBeInTheDocument();
    });

    // Verify PUT was called
    const putCalls = mockFetch.mock.calls.filter(
      ([url, init]: [string, RequestInit]) =>
        url === '/api/transactions/abc-123' && (init?.method ?? 'GET').toUpperCase() === 'PUT',
    );
    expect(putCalls.length).toBeGreaterThan(0);
  });

  it('Test 3: Delete → DELETE called → navigates to /movements', async () => {
    renderAt('/movements/abc-123/edit');

    await waitFor(() => {
      expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
    });

    // Click Delete button (opens dialog)
    const deleteButtons = await screen.findAllByRole('button', { name: /delete/i });
    fireEvent.click(deleteButtons[0]);

    // Confirm deletion in dialog — last Delete button is the confirm action
    const confirmButtons = await screen.findAllByRole('button', { name: /delete/i });
    fireEvent.click(confirmButtons[confirmButtons.length - 1]);

    // Should navigate to list page
    await waitFor(() => {
      expect(screen.getByTestId('list-page')).toBeInTheDocument();
    });

    // Verify DELETE was called
    const deleteCalls = mockFetch.mock.calls.filter(
      ([url, init]: [string, RequestInit]) =>
        url === '/api/transactions/abc-123' && (init?.method ?? 'GET').toUpperCase() === 'DELETE',
    );
    expect(deleteCalls.length).toBeGreaterThan(0);
  });

  it('Test 4: 422 on save → field errors visible', async () => {
    mockFetch.mockImplementation((url: string, init?: RequestInit) => {
      const method = (init?.method ?? 'GET').toUpperCase();
      if (method === 'PUT' && url === '/api/transactions/abc-123') {
        return Promise.resolve({
          ok: false,
          status: 422,
          json: async () => ({
            error: {
              code: 'VALIDATION_ERROR',
              message: 'Validation failed.',
              details: [{ field: 'Amount', message: 'Must be > 0' }],
            },
          }),
        });
      }
      return defaultFetchImpl(url, init);
    });

    renderAt('/movements/abc-123/edit');

    await waitFor(() => {
      expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
    });

    // Submit the form
    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() => {
      expect(screen.getByText(/must be > 0/i)).toBeInTheDocument();
    });
  });

});
