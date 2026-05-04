import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { Toaster } from 'sonner';
import { ReconciliationCard } from './ReconciliationCard';
import type { StagedTransactionDto } from './review-api';

function dto(overrides: Partial<StagedTransactionDto> = {}): StagedTransactionDto {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    importedAt: '2026-05-04T09:14:00Z',
    accountId: 'aaaa',
    accountName: 'Checking',
    accountCurrencyCode: 'EUR',
    accountCurrencySymbol: '€',
    rawDate: '2026-05-02',
    rawAmount: -500.00,
    rawDescription: 'Transfer to savings',
    matchedTransactionId: 'mmmm',
    matchedTransactionDescription: 'Wire to savings account',
    matchedTransactionDate: '2026-05-02',
    matchedTransactionAmount: -500.00,
    ...overrides,
  };
}

describe('ReconciliationCard', () => {
  beforeEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('renders meta, raw row, description, and matched pill', () => {
    render(<ReconciliationCard staged={dto()} onChanged={() => {}} />);
    expect(screen.getByText(/Checking — imported/)).toBeInTheDocument();
    expect(screen.getByText('Transfer to savings')).toBeInTheDocument();
    expect(screen.getByText(/Matched to:/)).toBeInTheDocument();
  });

  it('Confirm match POSTs to the confirm endpoint and fires success toast on 204', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 204 });
    vi.stubGlobal('fetch', fetchMock as typeof fetch);
    const onChanged = vi.fn();

    render(
      <>
        <Toaster />
        <ReconciliationCard staged={dto()} onChanged={onChanged} />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /Confirm match/i }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      '/api/reconciliation-review/11111111-1111-1111-1111-111111111111/confirm',
      expect.objectContaining({ method: 'POST' }),
    ));
    await waitFor(() => expect(onChanged).toHaveBeenCalled());
    await waitFor(() => expect(screen.getByText('Confirmed.')).toBeInTheDocument());
  });

  it('404 fires "no longer exists" toast and still calls onChanged so list refetches', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 404 }) as typeof fetch);
    const onChanged = vi.fn();
    render(
      <>
        <Toaster />
        <ReconciliationCard staged={dto()} onChanged={onChanged} />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /Confirm match/i }));
    await waitFor(() => expect(screen.getByText(/no longer exists/i)).toBeInTheDocument());
    expect(onChanged).toHaveBeenCalled();
  });

  it('other errors fire generic toast and do NOT call onChanged', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 500 }) as typeof fetch);
    const onChanged = vi.fn();
    render(
      <>
        <Toaster />
        <ReconciliationCard staged={dto()} onChanged={onChanged} />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /Confirm match/i }));
    await waitFor(() => expect(screen.getByText(/Couldn't confirm/i)).toBeInTheDocument());
    expect(onChanged).not.toHaveBeenCalled();
  });

  it('row menu opens and shows Dispute item which opens the dialog', async () => {
    render(<ReconciliationCard staged={dto()} onChanged={() => {}} />);
    fireEvent.click(screen.getByRole('button', { name: /More actions/i }));
    fireEvent.click(screen.getByText('Dispute'));
    await waitFor(() => expect(screen.getByText('Dispute this match?')).toBeInTheDocument());
  });
});
