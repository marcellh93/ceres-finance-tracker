import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';
import { toast } from 'sonner';
import { AccountEdit } from './AccountEdit';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

const mockFetch = vi.fn();

const accountDetail = {
  id: 'a-1', name: 'Checking Account',
  accountTypeId: 1, accountTypeName: 'Asset',
  currencyId: 1, currencyCode: 'EUR', currencySymbol: '€',
  description: 'Main checking', isActive: true, excludeFromSpendable: false,
  liabilityRepaymentType: null, interestRate: null,
  openingBalance: 1000, openingBalanceDate: '2026-01-01',
};

const accountTypesResponse = [
  { id: 1, name: 'Asset' },
  { id: 2, name: 'Liability' },
];
const currenciesResponse = [
  { id: 1, code: 'EUR', name: 'Euro', symbol: '€' },
];

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string, init?: RequestInit) => {
    if (url === '/api/account-types') return Promise.resolve({ ok: true, status: 200, json: async () => accountTypesResponse });
    if (url === '/api/currencies')    return Promise.resolve({ ok: true, status: 200, json: async () => currenciesResponse });
    if (url === '/api/accounts/a-1' && (!init || init.method === undefined)) {
      return Promise.resolve({ ok: true, status: 200, json: async () => accountDetail });
    }
    if (url === '/api/accounts/a-missing') {
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    }
    if (url === '/api/accounts/a-1' && init?.method === 'PATCH') {
      return Promise.resolve({ ok: true, status: 204, json: async () => null });
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
        <Route path="/accounts" element={<LayoutShim />}>
          <Route index element={<div data-testid="list-page">LIST</div>} />
          <Route path=":id/edit" element={<AccountEdit />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('AccountEdit', () => {
  it('renders the form pre-populated', async () => {
    renderPage('/accounts/a-1/edit');
    await waitFor(() => expect(screen.getByLabelText(/name/i)).toHaveValue('Checking Account'));
  });

  it('GET 404 renders the not-found banner', async () => {
    renderPage('/accounts/a-missing/edit');
    await waitFor(() =>
      expect(screen.getByText(/that account doesn't exist/i)).toBeInTheDocument(),
    );
    expect(screen.getByRole('link', { name: /back to accounts/i })).toBeInTheDocument();
  });

  it('PATCH success fires toast.success and navigates back', async () => {
    renderPage('/accounts/a-1/edit');
    await waitFor(() => expect(screen.getByLabelText(/name/i)).toHaveValue('Checking Account'));
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Renamed' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Saved.'));
    expect(await screen.findByTestId('list-page')).toBeInTheDocument();
  });

  it('PATCH 422 fires toast.error and form retains values', async () => {
    mockFetch.mockImplementation((url: string, init?: RequestInit) => {
      if (url === '/api/account-types') return Promise.resolve({ ok: true, status: 200, json: async () => accountTypesResponse });
      if (url === '/api/currencies')    return Promise.resolve({ ok: true, status: 200, json: async () => currenciesResponse });
      if (url === '/api/accounts/a-1' && (!init || init.method === undefined)) {
        return Promise.resolve({ ok: true, status: 200, json: async () => accountDetail });
      }
      if (url === '/api/accounts/a-1' && init?.method === 'PATCH') {
        return Promise.resolve({ ok: false, status: 422, json: async () => ({ error: { code: 'VALIDATION_ERROR', message: 'Bad', details: [] } }) });
      }
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderPage('/accounts/a-1/edit');
    await waitFor(() => expect(screen.getByLabelText(/name/i)).toHaveValue('Checking Account'));
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Renamed' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Couldn't save. Try again."));
    expect(screen.getByLabelText(/name/i)).toHaveValue('Renamed');
  });
});
