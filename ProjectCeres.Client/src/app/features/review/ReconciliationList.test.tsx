import { render, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ReconciliationList } from './ReconciliationList';

function mockGet(rows: unknown[]) {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
    ok: true, status: 200, json: async () => rows,
  }) as typeof fetch);
}

function mockGetError() {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 500 }) as typeof fetch);
}

const sampleRow = (id: string, amount: number) => ({
  id,
  importedAt: '2026-05-04T09:14:00Z',
  accountId: 'a',
  accountName: 'Checking',
  accountCurrencyCode: 'EUR',
  accountCurrencySymbol: '€',
  rawDate: '2026-05-02',
  rawAmount: amount,
  rawDescription: 'desc',
  matchedTransactionId: `m-${id}`,
  matchedTransactionDescription: null,
  matchedTransactionDate: '2026-05-02',
  matchedTransactionAmount: amount,
});

describe('ReconciliationList', () => {
  beforeEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('shows skeletons after the delay window when loading is slow', async () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})) as typeof fetch);
    render(<ReconciliationList onChanged={() => {}} />);
    expect(screen.queryAllByTestId('reconciliation-skeleton').length).toBe(0);
    await waitFor(
      () => expect(screen.getAllByTestId('reconciliation-skeleton').length).toBeGreaterThan(0),
      { timeout: 500 },
    );
  });

  it('renders one card per row', async () => {
    mockGet([sampleRow('1', -1), sampleRow('2', -2)]);
    render(<ReconciliationList onChanged={() => {}} />);
    await waitFor(() => expect(screen.getAllByRole('button', { name: /Confirm match/i }).length).toBe(2));
  });

  it('Confirm-all button visible when rows exist; opens dialog on click', async () => {
    mockGet([sampleRow('1', -1)]);
    render(<ReconciliationList onChanged={() => {}} />);
    const button = await screen.findByRole('button', { name: /^Confirm all$/i });
    await userEvent.click(button);
    // dialog title for count=1 is "Confirm all 1 matches?"
    await waitFor(() => expect(screen.getByText(/Confirm all 1 matches/)).toBeInTheDocument());
  });

  it('renders empty state when no rows; no Confirm-all button', async () => {
    mockGet([]);
    render(<ReconciliationList onChanged={() => {}} />);
    await waitFor(() => expect(screen.getByText(/No reconciliations to review/)).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: /^Confirm all$/i })).toBeNull();
  });

  it('renders CardError on fetch failure with retry', async () => {
    mockGetError();
    render(<ReconciliationList onChanged={() => {}} />);
    await waitFor(() => expect(screen.getByText(/Couldn't load Reconciliations/)).toBeInTheDocument());
    expect(screen.getByRole('button', { name: /Retry/i })).toBeInTheDocument();
  });
});
