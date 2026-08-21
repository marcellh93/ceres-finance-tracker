import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';
import { toast } from 'sonner';
import { AccountCreate } from './AccountCreate';
import { installCsrfFetchMock, resetCsrfCache } from '../../../test/csrf-fetch-mock';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

let mockFetch: ReturnType<typeof vi.fn>;

const accountTypesResponse = [
  { id: 1, name: 'Asset' },
  { id: 2, name: 'Liability' },
];

const currenciesResponse = [
  { id: 1, code: 'EUR', name: 'Euro',     symbol: '€' },
  { id: 2, code: 'USD', name: 'US Dollar', symbol: '$' },
];

beforeEach(async () => {
  await resetCsrfCache();
  mockFetch = installCsrfFetchMock();
  mockFetch.mockImplementation((url: string, init?: RequestInit) => {
    if (url === '/api/account-types') {
      return Promise.resolve({ ok: true, status: 200, json: async () => accountTypesResponse });
    }
    if (url === '/api/currencies') {
      return Promise.resolve({ ok: true, status: 200, json: async () => currenciesResponse });
    }
    if (url === '/api/accounts' && init?.method === 'POST') {
      return Promise.resolve({ ok: true, status: 201, json: async () => ({ id: 'new-1' }) });
    }
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

function LayoutShim() {
  return <Outlet context={{ refetch: vi.fn() }} />;
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/accounts/new']}>
      <Routes>
        <Route path="/accounts" element={<LayoutShim />}>
          <Route index element={<div data-testid="list-page">LIST</div>} />
          <Route path="new" element={<AccountCreate />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('AccountCreate', () => {
  it('renders the form when account-types and currencies load', async () => {
    renderPage();
    await screen.findByLabelText(/name/i);
    expect(screen.getByLabelText(/^type$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/currency/i)).toBeInTheDocument();
  });

  it('POST 2xx fires toast.success and navigates back', async () => {
    renderPage();
    const nameInput = await screen.findByLabelText(/name/i);
    fireEvent.change(nameInput, { target: { value: 'New Account' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Created.'));
    expect(await screen.findByTestId('list-page')).toBeInTheDocument();
  });

  it('POST 422 fires toast.error and form retains values', async () => {
    mockFetch.mockImplementation((url: string, init?: RequestInit) => {
      if (url === '/api/account-types') return Promise.resolve({ ok: true, status: 200, json: async () => accountTypesResponse });
      if (url === '/api/currencies')    return Promise.resolve({ ok: true, status: 200, json: async () => currenciesResponse });
      if (url === '/api/accounts' && init?.method === 'POST') {
        return Promise.resolve({ ok: false, status: 422, json: async () => ({ error: { code: 'VALIDATION_ERROR', message: 'Bad', details: [] } }) });
      }
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderPage();
    const nameInput = await screen.findByLabelText(/name/i);
    fireEvent.change(nameInput, { target: { value: 'New Account' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Couldn't save. Try again."),
    );
    expect(screen.getByLabelText(/name/i)).toHaveValue('New Account');
  });
});
