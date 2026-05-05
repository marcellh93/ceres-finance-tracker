import { render, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { Toaster } from 'sonner';
import { TransferCard } from './TransferCard';
import type { StagedTransferDto } from './review-api';

function dto(overrides: Partial<StagedTransferDto> = {}): StagedTransferDto {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    importedAt: '2026-05-04T09:14:00Z',
    accountId: 'aaaa',
    accountName: 'Checking',
    accountCurrencyCode: 'EUR',
    accountCurrencySymbol: '€',
    rawDate: '2026-05-02',
    rawAmount: -500.0,
    rawDescription: 'Transfer to savings',
    candidateTransactionId: 'cccc',
    candidateTransactionDescription: 'Incoming transfer',
    candidateTransactionDate: '2026-05-02',
    candidateTransactionAmount: 500.0,
    ...overrides,
  };
}

describe('TransferCard', () => {
  beforeEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('renders meta, raw row, and candidate pill when candidate present', () => {
    render(<TransferCard staged={dto()} accounts={[]} onChanged={() => {}} />);
    expect(screen.getByText(/Checking — imported/)).toBeInTheDocument();
    expect(screen.getByText(/Possible match:/)).toBeInTheDocument();
  });

  it('hides Link button and pill when no candidate', () => {
    render(
      <TransferCard
        staged={dto({
          candidateTransactionId: null,
          candidateTransactionDescription: null,
          candidateTransactionDate: null,
          candidateTransactionAmount: null,
        })}
        accounts={[]}
        onChanged={() => {}}
      />,
    );
    expect(screen.queryByText(/Possible match/)).toBeNull();
    expect(screen.queryByRole('button', { name: /Link to existing/i })).toBeNull();
    expect(screen.getByRole('button', { name: /Create transfer/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Dismiss/i })).toBeInTheDocument();
  });

  it('Dismiss POSTs to /dismiss-as-transaction; 204 fires success toast and onChanged', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 204 });
    vi.stubGlobal('fetch', fetchMock as typeof fetch);
    const onChanged = vi.fn();

    render(
      <>
        <Toaster />
        <TransferCard staged={dto()} accounts={[]} onChanged={onChanged} />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /Dismiss/i }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        `/api/transfer-review/${dto().id}/dismiss-as-transaction`,
        expect.objectContaining({ method: 'POST' }),
      ),
    );
    await waitFor(() => expect(onChanged).toHaveBeenCalled());
    await waitFor(() =>
      expect(screen.getByText('Imported as a plain transaction.')).toBeInTheDocument(),
    );
  });

  it('Dismiss shows "Dismissing…" and disables button during flight', async () => {
    let resolve!: (v: Response) => void;
    vi.stubGlobal(
      'fetch',
      vi.fn(
        () =>
          new Promise<Response>((r) => {
            resolve = r;
          }),
      ) as typeof fetch,
    );

    render(
      <>
        <Toaster />
        <TransferCard staged={dto()} accounts={[]} onChanged={() => {}} />
      </>,
    );
    const button = screen.getByRole('button', { name: /Dismiss/i });
    await userEvent.click(button);
    expect(button).toBeDisabled();
    expect(screen.getByText(/Dismissing…/)).toBeInTheDocument();

    resolve({ ok: true, status: 204 } as Response);
    await waitFor(() => expect(button).not.toBeDisabled());
  });

  it('rapid double-click on Dismiss fires only one POST', async () => {
    let resolve!: (v: Response) => void;
    const fetchMock = vi.fn(
      () =>
        new Promise<Response>((r) => {
          resolve = r;
        }),
    );
    vi.stubGlobal('fetch', fetchMock as typeof fetch);

    render(<TransferCard staged={dto()} accounts={[]} onChanged={() => {}} />);
    const button = screen.getByRole('button', { name: /Dismiss/i });
    await userEvent.click(button);
    await userEvent.click(button); // disabled now, should not fire

    expect(fetchMock).toHaveBeenCalledTimes(1);
    resolve({ ok: true, status: 204 } as Response);
  });

  it('Dismiss 404 fires "no longer exists" toast and calls onChanged', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({ ok: false, status: 404 }) as typeof fetch,
    );
    const onChanged = vi.fn();
    render(
      <>
        <Toaster />
        <TransferCard staged={dto()} accounts={[]} onChanged={onChanged} />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /Dismiss/i }));
    await waitFor(() =>
      expect(screen.getByText(/no longer exists/i)).toBeInTheDocument(),
    );
    expect(onChanged).toHaveBeenCalled();
  });

  it('Dismiss other error fires generic toast, does not call onChanged', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({ ok: false, status: 500 }) as typeof fetch,
    );
    const onChanged = vi.fn();
    render(
      <>
        <Toaster />
        <TransferCard staged={dto()} accounts={[]} onChanged={onChanged} />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /Dismiss/i }));
    await waitFor(() =>
      expect(screen.getByText(/Couldn't dismiss/i)).toBeInTheDocument(),
    );
    expect(onChanged).not.toHaveBeenCalled();
  });

  it('clicking Link to existing opens the link dialog', async () => {
    render(<TransferCard staged={dto()} accounts={[]} onChanged={() => {}} />);
    await userEvent.click(screen.getByRole('button', { name: /Link to existing/i }));
    await waitFor(() => expect(screen.getByText('Link to existing transfer')).toBeInTheDocument());
  });

  it('clicking Create transfer opens the create dialog', async () => {
    render(<TransferCard staged={dto()} accounts={[]} onChanged={() => {}} />);
    // The trigger button text "Create transfer" matches both the trigger and the dialog title.
    // Click via getByRole('button') which is unambiguous before the dialog opens.
    await userEvent.click(screen.getByRole('button', { name: /^Create transfer$/i }));
    // After click, BOTH the original trigger button AND the dialog title contain "Create transfer".
    // Assert the dialog opened by checking for body copy unique to the dialog.
    await waitFor(() => expect(screen.getByText(/We'll create a new transfer record/)).toBeInTheDocument());
  });
});
