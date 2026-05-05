import { render, screen, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { TransferList } from './TransferList';

function mockResponses(rows: unknown[], accounts: unknown[]) {
  vi.stubGlobal(
    'fetch',
    vi.fn((url: string) => {
      if (url.endsWith('/api/transfer-review/pending')) {
        return Promise.resolve({ ok: true, status: 200, json: async () => rows } as Response);
      }
      if (url.endsWith('/api/accounts')) {
        return Promise.resolve({ ok: true, status: 200, json: async () => accounts } as Response);
      }
      return Promise.reject(new Error(`Unexpected URL: ${url}`));
    }) as typeof fetch,
  );
}

const sampleRow = (id: string) => ({
  id,
  importedAt: '2026-05-04T09:14:00Z',
  accountId: 'a',
  accountName: 'Checking',
  accountCurrencyCode: 'EUR',
  accountCurrencySymbol: '€',
  rawDate: '2026-05-02',
  rawAmount: -1,
  rawDescription: 'a',
  candidateTransactionId: null,
  candidateTransactionDescription: null,
  candidateTransactionDate: null,
  candidateTransactionAmount: null,
});

describe('TransferList', () => {
  beforeEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('shows skeletons while loading', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})) as typeof fetch);
    render(<TransferList onChanged={() => {}} />);
    expect(screen.getAllByTestId('transfer-skeleton').length).toBeGreaterThan(0);
  });

  it('renders one card per row when loaded', async () => {
    mockResponses([sampleRow('1')], []);
    render(<TransferList onChanged={() => {}} />);
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /Dismiss/i })).toBeInTheDocument(),
    );
  });

  it('renders empty state when no rows', async () => {
    mockResponses([], []);
    render(<TransferList onChanged={() => {}} />);
    await waitFor(() =>
      expect(screen.getByText(/No transfers to review/)).toBeInTheDocument(),
    );
  });

  it('renders CardError on fetch failure', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 500 }) as typeof fetch);
    render(<TransferList onChanged={() => {}} />);
    await waitFor(() =>
      expect(screen.getByText(/Couldn't load Transfers/)).toBeInTheDocument(),
    );
  });
});
