import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Outlet, MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { MovementEdit } from './MovementEdit';

// ── Mock sonner ──
vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));
import { toast } from 'sonner';

// ── Mock AttachmentDropzone — assert props rather than render full DOM ──
vi.mock('./AttachmentDropzone', () => ({
  AttachmentDropzone: vi.fn((props: {
    parentType: string;
    parentId: string;
    pendingFiles?: File[];
    initialAttachments: Array<{ id: string }>;
  }) => (
    <div
      data-testid="dropzone"
      data-parent-type={props.parentType}
      data-parent-id={props.parentId}
      data-pending-files={(props.pendingFiles ?? []).map((f) => f.name).join(',')}
      data-pending-count={(props.pendingFiles ?? []).length}
      data-initial-count={props.initialAttachments.length}
    />
  )),
}));

// ── Mock fetch ──
let mockFetch: ReturnType<typeof vi.fn>;

const TRANSACTION_DTO = {
  id: 'abc-123',
  date: '2025-03-15',
  amount: 250.0,
  accountId: 'a1',
  categoryId: 'c1',
  description: 'March salary',
  isCleared: true,
  attachments: [
    {
      id: 'att-1',
      fileName: 'receipt.pdf',
      sizeBytes: 1234,
      contentType: 'application/pdf',
      uploadedAt: '2025-03-16T10:00:00Z',
    },
  ],
};

const TRANSFER_DTO = {
  id: 'abc-123',
  date: '2025-03-15',
  amount: 100.0,
  sourceAccountId: 'a1',
  destAccountId: 'a2',
  description: 'Move funds',
  isCleared: false,
  attachments: [],
};

const LIABILITY_PAYMENT_DTO = {
  id: 'abc-123',
  date: '2025-03-15',
  amount: 500.0,
  assetAccountId: 'a1',
  liabilityAccountId: 'a3',
  description: 'Loan payment',
  isCleared: true,
};

let discriminatorType: 'Transaction' | 'Transfer' | 'LiabilityPayment' = 'Transaction';

function defaultFetchImpl(url: string, init?: RequestInit) {
  const method = (init?.method ?? 'GET').toUpperCase();

  if (method === 'GET' && url === '/api/movements/abc-123') {
    return Promise.resolve({
      ok: true,
      status: 200,
      json: async () => ({ id: 'abc-123', movementType: discriminatorType }),
    });
  }
  if (method === 'GET' && url === '/api/transactions/abc-123') {
    return Promise.resolve({
      ok: true,
      status: 200,
      json: async () => TRANSACTION_DTO,
    });
  }
  if (method === 'GET' && url === '/api/transfers/abc-123') {
    return Promise.resolve({
      ok: true,
      status: 200,
      json: async () => TRANSFER_DTO,
    });
  }
  if (method === 'GET' && url === '/api/liability-payments/abc-123') {
    return Promise.resolve({
      ok: true,
      status: 200,
      json: async () => LIABILITY_PAYMENT_DTO,
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
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation(defaultFetchImpl);
  discriminatorType = 'Transaction';
});

afterEach(() => {
  // Do NOT use vi.resetAllMocks() — it wipes mockFetch's implementation mid-test
  // when other test files' afterEach hooks fire concurrently (cda7b04 root cause).
  // Only clear call history for mocks this file owns.
  vi.mocked(mockFetch).mockClear();
  vi.mocked(refetchMock).mockClear();
});

// ── Route helpers ──

const refetchMock = vi.fn();

function OutletShim() {
  const loc = useLocation();
  return (
    <>
      <div data-testid="probe-state">{JSON.stringify(loc.state)}</div>
      <div data-testid="probe-url">{loc.pathname + loc.search}</div>
      <Outlet context={{ refetch: refetchMock }} />
    </>
  );
}

function renderAt(
  path: string,
  state?: unknown,
) {
  return render(
    <MemoryRouter initialEntries={[{ pathname: path, state }]}>
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

  it('Test 5: dropzone NOT rendered when entity is a LiabilityPayment', async () => {
    discriminatorType = 'LiabilityPayment';
    renderAt('/movements/abc-123/edit');

    await waitFor(() => {
      expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
    });

    expect(screen.queryByTestId('dropzone')).not.toBeInTheDocument();
  });

  it('Test 6: dropzone IS rendered when entity is a Transaction', async () => {
    discriminatorType = 'Transaction';
    renderAt('/movements/abc-123/edit');

    await waitFor(() => {
      expect(screen.getByTestId('dropzone')).toBeInTheDocument();
    });

    const dz = screen.getByTestId('dropzone');
    expect(dz.dataset.parentType).toBe('Transaction');
    expect(dz.dataset.parentId).toBe('abc-123');
    // initialAttachments sourced from loaded DTO (TRANSACTION_DTO has 1)
    expect(dz.dataset.initialCount).toBe('1');
  });

  it('Test 7: dropzone IS rendered when entity is a Transfer', async () => {
    discriminatorType = 'Transfer';
    renderAt('/movements/abc-123/edit');

    await waitFor(() => {
      expect(screen.getByTestId('dropzone')).toBeInTheDocument();
    });

    const dz = screen.getByTestId('dropzone');
    expect(dz.dataset.parentType).toBe('Transfer');
    expect(dz.dataset.parentId).toBe('abc-123');
    expect(dz.dataset.initialCount).toBe('0');
  });

  it('Test 8: pendingAttachments[] from location.state is passed to dropzone as pendingFiles', async () => {
    discriminatorType = 'Transaction';
    const file = new File(['hello'], 'pending.txt', { type: 'text/plain' });
    renderAt('/movements/abc-123/edit', { pendingAttachments: [file] });

    await waitFor(() => {
      expect(screen.getByTestId('dropzone')).toBeInTheDocument();
    });

    const dz = screen.getByTestId('dropzone');
    expect(dz.dataset.pendingFiles).toBe('pending.txt');
    expect(dz.dataset.pendingCount).toBe('1');
  });

  it('Test 9: location state is cleared after mount', async () => {
    discriminatorType = 'Transaction';
    const file = new File(['hello'], 'pending.txt', { type: 'text/plain' });
    renderAt('/movements/abc-123/edit', { pendingAttachments: [file] });

    await waitFor(() => {
      expect(screen.getByTestId('dropzone')).toBeInTheDocument();
    });

    await waitFor(() => {
      const probe = screen.getByTestId('probe-state');
      // After clear, state should be {} (or at least no pendingAttachments)
      const parsed = probe.textContent ? JSON.parse(probe.textContent) : null;
      expect(parsed?.pendingAttachments).toBeUndefined();
    });
  });

  it('Save navigates back to /movements with the entity\'s currency code in the URL', async () => {
    renderAt('/movements/abc-123/edit');

    await waitFor(() => {
      expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() => {
      expect(toast.success).toHaveBeenCalledWith('Saved.');
    });

    // The fixture account a1 has currencyCode: 'EUR' → URL should reflect it.
    await waitFor(() => {
      expect(screen.getByTestId('probe-url').textContent).toBe('/movements?currency=EUR');
    });
  });

  it('Delete navigates back to /movements with the entity\'s currency code in the URL', async () => {
    renderAt('/movements/abc-123/edit');

    await waitFor(() => {
      expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
    });

    const deleteButtons = await screen.findAllByRole('button', { name: /delete/i });
    fireEvent.click(deleteButtons[0]);
    const confirmButtons = await screen.findAllByRole('button', { name: /delete/i });
    fireEvent.click(confirmButtons[confirmButtons.length - 1]);

    await waitFor(() => {
      expect(screen.getByTestId('probe-url').textContent).toBe('/movements?currency=EUR');
    });
  });

});
