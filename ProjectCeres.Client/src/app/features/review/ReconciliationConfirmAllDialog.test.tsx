import { render, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { Toaster } from 'sonner';
import { ReconciliationConfirmAllDialog } from './ReconciliationConfirmAllDialog';

describe('ReconciliationConfirmAllDialog', () => {
  beforeEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('title shows the captured count from props', () => {
    render(
      <ReconciliationConfirmAllDialog
        open count={5}
        onOpenChange={() => {}} onConfirmed={() => {}}
      />,
    );
    expect(screen.getByText(/Confirm all 5 matches/)).toBeInTheDocument();
  });

  it('submit posts to /confirm-all and on 204 toasts with the count, calls onConfirmed, closes', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 204 });
    vi.stubGlobal('fetch', fetchMock as typeof fetch);
    const onConfirmed = vi.fn();
    const onOpenChange = vi.fn();

    render(
      <>
        <Toaster />
        <ReconciliationConfirmAllDialog
          open count={3}
          onOpenChange={onOpenChange} onConfirmed={onConfirmed}
        />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /^Confirm all$/i }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      '/api/reconciliation-review/confirm-all',
      expect.objectContaining({ method: 'POST' }),
    ));
    await waitFor(() => expect(screen.getByText('Confirmed 3 matches.')).toBeInTheDocument());
    expect(onConfirmed).toHaveBeenCalled();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('on error keeps the dialog open and shows generic toast', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 500 }) as typeof fetch);
    const onOpenChange = vi.fn();
    render(
      <>
        <Toaster />
        <ReconciliationConfirmAllDialog
          open count={3}
          onOpenChange={onOpenChange} onConfirmed={() => {}}
        />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /^Confirm all$/i }));
    await waitFor(() => expect(screen.getByText(/Couldn't confirm all/i)).toBeInTheDocument());
    // dialog must NOT close on error
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
