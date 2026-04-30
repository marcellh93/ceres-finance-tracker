import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Outlet, MemoryRouter, Route, Routes } from 'react-router-dom';
import { MovementCreate } from './MovementCreate';

// ── Mock sonner so toast calls don't explode in tests ──
vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

// ── Mock fetch ──
const mockFetch = vi.fn();

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;

  // Default: accounts + categories resolve with test data
  mockFetch.mockImplementation((url: string) => {
    if (url === '/api/accounts/active') {
      return Promise.resolve({
        ok: true,
        json: async () => [
          { id: 'a1', name: 'Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
          { id: 'a2', name: 'Credit Card', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Liability' },
        ],
      });
    }
    if (url === '/api/categories/active') {
      return Promise.resolve({
        ok: true,
        json: async () => [
          { id: 'c1', name: 'Salary', categoryTypeName: 'Income' },
          { id: 'c2', name: 'Groceries', categoryTypeName: 'Expense' },
        ],
      });
    }
    return Promise.resolve({ ok: false, status: 500, json: async () => ({}) });
  });
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
          <Route path="new" element={<MovementCreate />} />
          <Route path=":id/edit" element={<div data-testid="edit-page">EDIT</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

// ── Tests ──

describe('MovementCreate', () => {
  it('Test 1: renders 3-card picker at /movements/new (no ?type= query)', () => {
    renderAt('/movements/new');

    // Three cards for the three movement types
    expect(screen.getByRole('button', { name: /transaction/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /transfer/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /liability payment/i })).toBeInTheDocument();
  });

  it('Test 2: clicking Transaction card updates URL to ?type=transaction', async () => {
    renderAt('/movements/new');

    fireEvent.click(screen.getByRole('button', { name: /transaction/i }));

    // After click, the form should appear (URL updated → MovementForm rendered)
    // The date field is present in MovementForm
    await waitFor(() => {
      expect(screen.getByLabelText(/date/i)).toBeInTheDocument();
    });
  });

  it('Test 3: with ?type=transaction, renders MovementForm with a Transaction-specific field', async () => {
    renderAt('/movements/new?type=transaction');

    // Account combobox is Transaction-specific
    await waitFor(() => {
      expect(screen.getByText(/select account/i)).toBeInTheDocument();
    });
    // Date field
    expect(screen.getByLabelText(/date/i)).toBeInTheDocument();
    // No picker cards
    expect(screen.queryByRole('button', { name: /^transaction$/i })).not.toBeInTheDocument();
  });

  it('Test 4: successful POST → navigates to /movements/<id>/edit?created=1', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/accounts/active') {
        return Promise.resolve({
          ok: true,
          json: async () => [
            { id: 'a1', name: 'Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
          ],
        });
      }
      if (url === '/api/categories/active') {
        return Promise.resolve({
          ok: true,
          json: async () => [
            { id: 'c1', name: 'Salary', categoryTypeName: 'Income' },
          ],
        });
      }
      if (url === '/api/transactions') {
        return Promise.resolve({
          ok: true,
          status: 201,
          json: async () => ({ id: 'new-id' }),
        });
      }
      return Promise.resolve({ ok: false, status: 500, json: async () => ({}) });
    });

    renderAt('/movements/new?type=transaction');

    // Wait for form to render with accounts/categories loaded
    await waitFor(() => {
      expect(screen.getByLabelText(/date/i)).toBeInTheDocument();
    });

    // Fill in required fields
    const amountInput = screen.getByLabelText(/amount/i);
    fireEvent.change(amountInput, { target: { value: '100' } });

    // Submit
    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    // Navigates to edit page
    await waitFor(() => {
      expect(screen.getByTestId('edit-page')).toBeInTheDocument();
    });
  });

  it('Test 5: 422 response → field errors visible in form', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/accounts/active') {
        return Promise.resolve({
          ok: true,
          json: async () => [
            { id: 'a1', name: 'Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
          ],
        });
      }
      if (url === '/api/categories/active') {
        return Promise.resolve({
          ok: true,
          json: async () => [
            { id: 'c1', name: 'Salary', categoryTypeName: 'Income' },
          ],
        });
      }
      if (url === '/api/transactions') {
        return Promise.resolve({
          ok: false,
          status: 422,
          json: async () => ({
            error: {
              code: 'VALIDATION_ERROR',
              message: 'Validation failed.',
              details: [{ field: 'Amount', message: 'Must be greater than zero.' }],
            },
          }),
        });
      }
      return Promise.resolve({ ok: false, status: 500, json: async () => ({}) });
    });

    renderAt('/movements/new?type=transaction');

    await waitFor(() => {
      expect(screen.getByLabelText(/date/i)).toBeInTheDocument();
    });

    // Submit without filling fields
    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() => {
      expect(screen.getByText(/must be greater than zero/i)).toBeInTheDocument();
    });
  });
});
