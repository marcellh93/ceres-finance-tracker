import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Outlet, MemoryRouter, Route, Routes } from 'react-router-dom';
import { MovementCreate } from './MovementCreate';

// ── Mock sonner so toast calls don't explode in tests ──
vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

// ── Mock useNavigate so we can assert destination + state ──
const navigateMock = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => navigateMock };
});

// ── Mock AttachmentDropzone — capture props so tests can drive onPendingChange ──
const lastDropzoneProps: { current: null | {
  mode?: string;
  parentType?: string;
  onPendingChange?: (files: File[]) => void;
} } = { current: null };

vi.mock('./AttachmentDropzone', () => ({
  AttachmentDropzone: (props: {
    mode?: string;
    parentType?: string;
    onPendingChange?: (files: File[]) => void;
  }) => {
    lastDropzoneProps.current = props;
    return (
      <div
        data-testid="dropzone"
        data-mode={props.mode}
        data-parent-type={props.parentType}
      />
    );
  },
}));

// ── Mock fetch ──
let mockFetch: ReturnType<typeof vi.fn>;

beforeEach(() => {
  mockFetch = vi.fn();
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
  navigateMock.mockReset();
  lastDropzoneProps.current = null;
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
          <Route index element={<div data-testid="list-page">LIST</div>} />
          <Route path="new" element={<MovementCreate />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

// Helper: configure fetch to return a 201 with given id for transactions create.
function mockTransactionCreate(newId: string) {
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
        json: async () => ({ id: newId }),
      });
    }
    return Promise.resolve({ ok: false, status: 500, json: async () => ({}) });
  });
}

// ── Tests ──

describe('MovementCreate', () => {
  it('Test 1: bounces to /movements when ?type= is missing (no picker)', async () => {
    renderAt('/movements/new');
    await waitFor(() => {
      expect(navigateMock).toHaveBeenCalledWith('/movements', { replace: true });
    });
  });

  it('Test 2: with ?type=transaction, renders MovementForm with a Transaction-specific field', async () => {
    renderAt('/movements/new?type=transaction');

    await waitFor(() => {
      expect(screen.getByText(/select account/i)).toBeInTheDocument();
    });
    expect(screen.getByLabelText(/date/i)).toBeInTheDocument();
  });

  it('Test 3: with ?type=transfer, renders source/destination account fields', async () => {
    renderAt('/movements/new?type=transfer');

    await waitFor(() => {
      expect(screen.getByText(/select source/i)).toBeInTheDocument();
    });
    expect(screen.getByText(/select destination/i)).toBeInTheDocument();
  });

  it('Test 4: successful POST with no pending files → navigates to /movements list', async () => {
    mockTransactionCreate('new-id');

    renderAt('/movements/new?type=transaction');

    await waitFor(() => {
      expect(screen.getByLabelText(/date/i)).toBeInTheDocument();
    });

    fireEvent.change(screen.getByLabelText(/amount/i), { target: { value: '100' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() => {
      expect(navigateMock).toHaveBeenCalledWith('/movements', { replace: true });
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

    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() => {
      expect(screen.getByText(/must be greater than zero/i)).toBeInTheDocument();
    });
  });

  // ── Task 5: Pending attachment hand-off ──

  it('Test 6: AttachmentDropzone is rendered in create mode and no upload API was hit', async () => {
    renderAt('/movements/new?type=transaction');

    await waitFor(() => {
      expect(screen.getByLabelText(/date/i)).toBeInTheDocument();
    });

    const dz = await screen.findByTestId('dropzone');
    expect(dz.dataset.mode).toBe('create');
    expect(dz.dataset.parentType).toBe('Transaction');

    // No upload endpoints should have been hit. Only accounts + categories fetches.
    const calledUrls = mockFetch.mock.calls.map((c) => c[0] as string);
    expect(calledUrls.some((u) => u.includes('/attachments'))).toBe(false);
  });

  it('Test 7: Picking files via dropzone then submitting → navigate carries pendingAttachments[]', async () => {
    mockTransactionCreate('new-id');

    renderAt('/movements/new?type=transaction');

    await waitFor(() => {
      expect(screen.getByLabelText(/date/i)).toBeInTheDocument();
    });

    // Wait for dropzone mount and capture its onPendingChange.
    await screen.findByTestId('dropzone');
    expect(lastDropzoneProps.current?.onPendingChange).toBeTruthy();

    const file = new File(['hello'], 'receipt.pdf', { type: 'application/pdf' });
    lastDropzoneProps.current!.onPendingChange!([file]);

    fireEvent.change(screen.getByLabelText(/amount/i), { target: { value: '100' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() => {
      expect(navigateMock).toHaveBeenCalledWith(
        '/movements/new-id/edit?created=1',
        expect.objectContaining({
          replace: true,
          state: { pendingAttachments: [file] },
        }),
      );
    });
  });

  it('Test 8: Successful save with no file → navigates to /movements list (no edit redirect)', async () => {
    mockTransactionCreate('new-id');

    renderAt('/movements/new?type=transaction');

    await waitFor(() => {
      expect(screen.getByLabelText(/date/i)).toBeInTheDocument();
    });

    fireEvent.change(screen.getByLabelText(/amount/i), { target: { value: '100' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() => {
      expect(navigateMock).toHaveBeenCalledWith('/movements', { replace: true });
    });

    const editCall = navigateMock.mock.calls.find(
      (c) => typeof c[0] === 'string' && (c[0] as string).startsWith('/movements/new-id/edit'),
    );
    expect(editCall).toBeFalsy();
  });
});
