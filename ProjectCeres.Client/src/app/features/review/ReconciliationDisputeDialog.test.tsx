import { render, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { Toaster } from 'sonner';
import { ReconciliationDisputeDialog } from './ReconciliationDisputeDialog';

const STAGED_ID = '11111111-1111-1111-1111-111111111111';

describe('ReconciliationDisputeDialog', () => {
  beforeEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('Cancel closes without firing fetch', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock as typeof fetch);
    const onOpenChange = vi.fn();
    render(
      <ReconciliationDisputeDialog
        open stagedId={STAGED_ID}
        onOpenChange={onOpenChange} onDisputed={() => {}}
      />,
    );
    await userEvent.click(screen.getByRole('button', { name: /Cancel/i }));
    expect(fetchMock).not.toHaveBeenCalled();
    // base-ui calls onOpenChange(false, eventDetails); assert on the first arg only.
    expect(onOpenChange).toHaveBeenCalled();
    expect(onOpenChange.mock.calls[0][0]).toBe(false);
  });

  it('Submit posts to /dispute and 204 fires success toast + onDisputed + close', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 204 });
    vi.stubGlobal('fetch', fetchMock as typeof fetch);
    const onDisputed = vi.fn();
    const onOpenChange = vi.fn();

    render(
      <>
        <Toaster />
        <ReconciliationDisputeDialog
          open stagedId={STAGED_ID}
          onOpenChange={onOpenChange} onDisputed={onDisputed}
        />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /Dispute match/i }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      `/api/reconciliation-review/${STAGED_ID}/dispute`,
      expect.objectContaining({ method: 'POST' }),
    ));
    await waitFor(() => expect(screen.getByText(/Disputed\. Original un-cleared and a new transaction added\./)).toBeInTheDocument());
    expect(onDisputed).toHaveBeenCalled();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('404 closes the dialog and still calls onDisputed (so the list refetches)', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 404 }) as typeof fetch);
    const onDisputed = vi.fn();
    const onOpenChange = vi.fn();
    render(
      <>
        <Toaster />
        <ReconciliationDisputeDialog
          open stagedId={STAGED_ID}
          onOpenChange={onOpenChange} onDisputed={onDisputed}
        />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /Dispute match/i }));
    await waitFor(() => expect(screen.getByText(/no longer exists/i)).toBeInTheDocument());
    expect(onDisputed).toHaveBeenCalled();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('422 keeps dialog open with generic error toast', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 422 }) as typeof fetch);
    const onOpenChange = vi.fn();
    render(
      <>
        <Toaster />
        <ReconciliationDisputeDialog
          open stagedId={STAGED_ID}
          onOpenChange={onOpenChange} onDisputed={() => {}}
        />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /Dispute match/i }));
    await waitFor(() => expect(screen.getByText(/Couldn't dispute/i)).toBeInTheDocument());
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
