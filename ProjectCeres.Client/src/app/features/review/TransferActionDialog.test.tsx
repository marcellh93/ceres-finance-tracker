import { render, screen, waitFor } from '@testing-library/react';
import { fireEvent } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { Toaster } from 'sonner';
import { TransferActionDialog } from './TransferActionDialog';
import type { AccountOption } from './TransferCard';
import { primeCsrfToken } from '../../../test/csrf-fetch-mock';
import { stubResponse } from '../../../test/csrf-fetch-mock';

const STAGED_ID = '11111111-1111-1111-1111-111111111111';

const accounts: AccountOption[] = [
  { id: 'aa', name: 'Checking',     isActive: true,  currencyCode: 'EUR' },
  { id: 'bb', name: 'Savings',      isActive: true,  currencyCode: 'EUR' },
  { id: 'cc', name: 'USD Account',  isActive: true,  currencyCode: 'USD' },
  { id: 'dd', name: 'Old Closed',   isActive: false, currencyCode: 'EUR' },
];

describe('TransferActionDialog', () => {
  beforeEach(async () => {
  await primeCsrfToken();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('mode="link" renders correct title and submit-button label', () => {
    render(
      <TransferActionDialog
        open mode="link" stagedId={STAGED_ID}
        ownAccountId="aa" ownAccountCurrencyCode="EUR"
        accounts={accounts}
        onOpenChange={() => {}} onActioned={() => {}}
      />,
    );
    expect(screen.getByText(/Link to existing transfer/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Link transfer$/i })).toBeInTheDocument();
  });

  it('mode="create" renders correct title and submit-button label', () => {
    render(
      <TransferActionDialog
        open mode="create" stagedId={STAGED_ID}
        ownAccountId="aa" ownAccountCurrencyCode="EUR"
        accounts={accounts}
        onOpenChange={() => {}} onActioned={() => {}}
      />,
    );
    // Both the dialog title and the action button render "Create transfer", so getByText is
    // ambiguous — assert at least one match and that the action button is present.
    expect(screen.getAllByText(/^Create transfer$/).length).toBeGreaterThan(0);
    const submits = screen.getAllByRole('button', { name: /^Create transfer$/i });
    expect(submits.length).toBeGreaterThan(0);
  });

  it('picker filters out own account, inactive accounts, and different-currency accounts', async () => {
    render(
      <TransferActionDialog
        open mode="create" stagedId={STAGED_ID}
        ownAccountId="aa" ownAccountCurrencyCode="EUR"
        accounts={accounts}
        onOpenChange={() => {}} onActioned={() => {}}
      />,
    );
    fireEvent.click(screen.getByRole('combobox'));
    await waitFor(() => expect(screen.getByText('Savings')).toBeInTheDocument());
    expect(screen.queryByText('Checking')).toBeNull();        // own account excluded
    expect(screen.queryByText('USD Account')).toBeNull();     // wrong currency excluded
    expect(screen.queryByText('Old Closed')).toBeNull();      // inactive excluded
  });

  it('shows empty-state in picker when no eligible accounts', async () => {
    render(
      <TransferActionDialog
        open mode="create" stagedId={STAGED_ID}
        ownAccountId="aa" ownAccountCurrencyCode="EUR"
        accounts={[
          { id: 'aa', name: 'Checking', isActive: true, currencyCode: 'EUR' },   // own
          { id: 'cc', name: 'USD',      isActive: true, currencyCode: 'USD' },   // wrong currency
        ]}
        onOpenChange={() => {}} onActioned={() => {}}
      />,
    );
    fireEvent.click(screen.getByRole('combobox'));
    await waitFor(() => expect(screen.getByText(/No eligible accounts/i)).toBeInTheDocument());
  });

  it('submit button disabled until selection made', () => {
    render(
      <TransferActionDialog
        open mode="link" stagedId={STAGED_ID}
        ownAccountId="aa" ownAccountCurrencyCode="EUR"
        accounts={accounts}
        onOpenChange={() => {}} onActioned={() => {}}
      />,
    );
    expect(screen.getByRole('button', { name: /^Link transfer$/i })).toBeDisabled();
  });

  it('mode="link" 204 success: posts to /link-to-existing, toast "Linked.", closes', async () => {
    const fetchMock = vi.fn().mockResolvedValue(stubResponse({ ok: true, status: 204 }));
    vi.stubGlobal('fetch', fetchMock as typeof fetch);
    const onActioned = vi.fn();
    const onOpenChange = vi.fn();

    render(
      <>
        <Toaster />
        <TransferActionDialog
          open mode="link" stagedId={STAGED_ID}
          ownAccountId="aa" ownAccountCurrencyCode="EUR"
          accounts={accounts}
          onOpenChange={onOpenChange} onActioned={onActioned}
        />
      </>,
    );
    fireEvent.click(screen.getByRole('combobox'));
    await waitFor(() => expect(screen.getByText('Savings')).toBeInTheDocument());
    fireEvent.click(screen.getByText('Savings'));
    await userEvent.click(screen.getByRole('button', { name: /^Link transfer$/i }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      `/api/transfer-review/${STAGED_ID}/link-to-existing`,
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ otherAccountId: 'bb' }),
      }),
    ));
    await waitFor(() => expect(screen.getByText('Linked.')).toBeInTheDocument());
    expect(onActioned).toHaveBeenCalled();
    expect(onOpenChange.mock.calls.some((args) => args[0] === false)).toBe(true);
  });

  it('mode="create" 204 success: posts to /create-as-transfer, toast "Transfer created.", closes', async () => {
    const fetchMock = vi.fn().mockResolvedValue(stubResponse({ ok: true, status: 204 }));
    vi.stubGlobal('fetch', fetchMock as typeof fetch);

    render(
      <>
        <Toaster />
        <TransferActionDialog
          open mode="create" stagedId={STAGED_ID}
          ownAccountId="aa" ownAccountCurrencyCode="EUR"
          accounts={accounts}
          onOpenChange={() => {}} onActioned={() => {}}
        />
      </>,
    );
    fireEvent.click(screen.getByRole('combobox'));
    await waitFor(() => expect(screen.getByText('Savings')).toBeInTheDocument());
    fireEvent.click(screen.getByText('Savings'));
    // Two "Create transfer" buttons — the action button is the LAST one (after the trigger).
    const submits = screen.getAllByRole('button', { name: /^Create transfer$/i });
    await userEvent.click(submits[submits.length - 1]);

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      `/api/transfer-review/${STAGED_ID}/create-as-transfer`,
      expect.objectContaining({ method: 'POST' }),
    ));
    await waitFor(() => expect(screen.getByText('Transfer created.')).toBeInTheDocument());
  });

  it('404 closes dialog and calls onActioned (so list refetches)', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(stubResponse({ ok: false, status: 404 })) as typeof fetch);
    const onActioned = vi.fn();
    const onOpenChange = vi.fn();
    render(
      <>
        <Toaster />
        <TransferActionDialog
          open mode="link" stagedId={STAGED_ID}
          ownAccountId="aa" ownAccountCurrencyCode="EUR"
          accounts={accounts}
          onOpenChange={onOpenChange} onActioned={onActioned}
        />
      </>,
    );
    fireEvent.click(screen.getByRole('combobox'));
    await waitFor(() => expect(screen.getByText('Savings')).toBeInTheDocument());
    fireEvent.click(screen.getByText('Savings'));
    await userEvent.click(screen.getByRole('button', { name: /^Link transfer$/i }));
    await waitFor(() => expect(screen.getByText(/no longer exists/i)).toBeInTheDocument());
    expect(onActioned).toHaveBeenCalled();
    expect(onOpenChange.mock.calls.some((args) => args[0] === false)).toBe(true);
  });

  it('422 keeps dialog open with generic error toast', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: false, status: 422,
      json: async () => ({ error: { code: 'VALIDATION_ERROR', message: 'oops', details: [] } }),
    }) as typeof fetch);
    const onOpenChange = vi.fn();
    render(
      <>
        <Toaster />
        <TransferActionDialog
          open mode="link" stagedId={STAGED_ID}
          ownAccountId="aa" ownAccountCurrencyCode="EUR"
          accounts={accounts}
          onOpenChange={onOpenChange} onActioned={() => {}}
        />
      </>,
    );
    fireEvent.click(screen.getByRole('combobox'));
    await waitFor(() => expect(screen.getByText('Savings')).toBeInTheDocument());
    fireEvent.click(screen.getByText('Savings'));
    await userEvent.click(screen.getByRole('button', { name: /^Link transfer$/i }));
    await waitFor(() => expect(screen.getByText(/Couldn't link/i)).toBeInTheDocument());
    expect(onOpenChange.mock.calls.some((args) => args[0] === false)).toBe(false);
  });

  it('Cancel closes without firing fetch', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock as typeof fetch);
    const onOpenChange = vi.fn();
    render(
      <TransferActionDialog
        open mode="link" stagedId={STAGED_ID}
        ownAccountId="aa" ownAccountCurrencyCode="EUR"
        accounts={accounts}
        onOpenChange={onOpenChange} onActioned={() => {}}
      />,
    );
    await userEvent.click(screen.getByRole('button', { name: /^Cancel$/i }));
    expect(fetchMock).not.toHaveBeenCalled();
    // base-ui passes (false, eventDetails) to onOpenChange on Cancel
    expect(onOpenChange.mock.calls.some((args) => args[0] === false)).toBe(true);
  });
});
