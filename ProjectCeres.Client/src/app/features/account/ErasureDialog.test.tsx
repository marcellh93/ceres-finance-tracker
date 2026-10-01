import { render, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { I18nextProvider } from 'react-i18next';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n/i18n';
import { ErasureDialog } from './ErasureDialog';

function renderDialog() {
  return render(
    <I18nextProvider i18n={i18n}>
      <ErasureDialog />
    </I18nextProvider>,
  );
}

const { toastError, toastSuccess } = vi.hoisted(() => ({
  toastError: vi.fn(),
  toastSuccess: vi.fn(),
}));
vi.mock('sonner', () => ({ toast: { error: toastError, success: toastSuccess } }));

// Pass-through step-up: this file's job is to gate the typed confirm and POST;
// reauth routing itself is exercised in use-step-up's own suite.
const requireStepUp = vi.fn(<T,>(action: () => Promise<T>) => action());
vi.mock('../../auth/use-step-up', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../auth/use-step-up')>()),
  useStepUp: () => ({ requireStepUp }),
}));

const requestErasure = vi.fn();
vi.mock('./account-api', () => ({
  ERASURE_URL: '/api/profile/erasure',
  requestErasure: () => requestErasure(),
}));

async function openDialog() {
  await userEvent.click(screen.getByRole('button', { name: /erase my account/i }));
}

function dialogConfirmButton() {
  const buttons = screen.getAllByRole('button', { name: /erase my account/i });
  return buttons[buttons.length - 1];
}

// Types the exact confirm phrase and waits out the deliberate enable delay
// (CONFIRM_ENABLE_DELAY_MS) using real timers — userEvent's internal delays
// don't compose reliably with fake timers, and this delay exists specifically
// to add real friction, so waiting it out for real keeps the test honest.
async function typePhraseAndWaitForEnable(user: ReturnType<typeof userEvent.setup>) {
  const input = screen.getByLabelText(/type "erase" to confirm/i);
  await user.type(input, 'ERASE');
  await waitFor(() => expect(dialogConfirmButton()).toBeEnabled(), { timeout: 2000 });
}

describe('ErasureDialog', () => {
  beforeEach(() => vi.clearAllMocks());

  it('opens on trigger click with the confirm action disabled', async () => {
    renderDialog();

    await openDialog();

    expect(screen.getByText(/erase your account permanently/i)).toBeInTheDocument();
    expect(dialogConfirmButton()).toBeDisabled();
  });

  it('keeps the confirm action disabled for anything other than the exact phrase', async () => {
    const user = userEvent.setup();
    renderDialog();
    await openDialog();

    const input = screen.getByLabelText(/type "erase" to confirm/i);

    await user.type(input, 'erase'); // lowercase — server check is case-sensitive
    expect(dialogConfirmButton()).toBeDisabled();

    await user.clear(input);
    await user.type(input, 'ERASED');
    expect(dialogConfirmButton()).toBeDisabled();
  });

  it('shows a case-sensitivity hint once text is typed but does not yet match', async () => {
    const user = userEvent.setup();
    renderDialog();
    await openDialog();

    await user.type(screen.getByLabelText(/type "erase" to confirm/i), 'erase');

    expect(screen.getByText(/must match exactly/i)).toBeInTheDocument();
  });

  it('associates the mismatch hint with the input via aria-describedby, and marks it aria-invalid', async () => {
    const user = userEvent.setup();
    renderDialog();
    await openDialog();

    const input = screen.getByLabelText(/type "erase" to confirm/i);
    expect(input).not.toHaveAttribute('aria-invalid', 'true');

    await user.type(input, 'erase');

    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(input).toHaveAttribute('aria-describedby', 'erasure-confirm-error');
    expect(screen.getByText(/must match exactly/i)).toHaveAttribute('id', 'erasure-confirm-error');
  });

  it('does not enable the confirm action immediately on a matching keystroke — a deliberate beat is required', async () => {
    const user = userEvent.setup();
    renderDialog();
    await openDialog();

    await user.type(screen.getByLabelText(/type "erase" to confirm/i), 'ERASE');

    // Immediately after the matching keystroke, before the enable delay elapses.
    expect(dialogConfirmButton()).toBeDisabled();
  });

  it('enables the confirm action after the beat, then requests erasure', async () => {
    const user = userEvent.setup();
    requestErasure.mockResolvedValue({ ok: true, status: 202, data: { message: 'Erasure scheduled.' } });
    renderDialog();
    await openDialog();

    await typePhraseAndWaitForEnable(user);
    await user.click(dialogConfirmButton());

    await waitFor(() => expect(requestErasure).toHaveBeenCalledOnce());
    expect(toastSuccess).toHaveBeenCalledWith('Erasure scheduled.');
    expect(toastError).not.toHaveBeenCalled();
  });

  it('shows a persistent pending banner after a successful request', async () => {
    const user = userEvent.setup();
    requestErasure.mockResolvedValue({ ok: true, status: 202, data: { message: 'Erasure scheduled.' } });
    renderDialog();
    await openDialog();

    await typePhraseAndWaitForEnable(user);
    await user.click(dialogConfirmButton());

    expect(await screen.findByRole('status')).toHaveTextContent(/erasure is scheduled/i);
  });

  it('closes and clears the typed text after a successful request', async () => {
    const user = userEvent.setup();
    requestErasure.mockResolvedValue({ ok: true, status: 202, data: { message: 'Erasure scheduled.' } });
    renderDialog();
    await openDialog();

    await typePhraseAndWaitForEnable(user);
    await user.click(dialogConfirmButton());

    await waitFor(() => expect(screen.queryByText(/erase your account permanently/i)).not.toBeInTheDocument());

    await openDialog();
    expect(screen.getByLabelText(/type "erase" to confirm/i)).toHaveValue('');
  });

  it('shows the daily-limit toast on 429 and keeps the dialog open', async () => {
    const user = userEvent.setup();
    requestErasure.mockResolvedValue({ ok: false, status: 429, code: 'RATE_LIMITED', message: '' });
    renderDialog();
    await openDialog();

    await typePhraseAndWaitForEnable(user);
    await user.click(dialogConfirmButton());

    await waitFor(() => expect(toastError).toHaveBeenCalledWith(expect.stringContaining('once per day')));
    expect(screen.getByText(/erase your account permanently/i)).toBeInTheDocument();
    expect(toastSuccess).not.toHaveBeenCalled();
  });

  it('distinguishes a 422 confirm-gate desync from a generic failure', async () => {
    const user = userEvent.setup();
    requestErasure.mockResolvedValue({ ok: false, status: 422, code: 'VALIDATION_ERROR', message: '' });
    renderDialog();
    await openDialog();

    await typePhraseAndWaitForEnable(user);
    await user.click(dialogConfirmButton());

    await waitFor(() => expect(toastError).toHaveBeenCalledWith(expect.stringContaining('out of sync')));
  });

  it('shows a generic error toast on other failures, distinct from the 422 message', async () => {
    // Regression guard: the 422 branch must be gated on the actual status
    // code, not a catch-all "else" — a true 500 saying "out of sync, refresh
    // the page" would mislead the user about what actually failed.
    const user = userEvent.setup();
    requestErasure.mockResolvedValue({ ok: false, status: 500, code: 'ERROR', message: '' });
    renderDialog();
    await openDialog();

    await typePhraseAndWaitForEnable(user);
    await user.click(dialogConfirmButton());

    await waitFor(() => expect(toastError).toHaveBeenCalledWith(expect.stringContaining('try again')));
    expect(toastError).not.toHaveBeenCalledWith(expect.stringContaining('out of sync'));
  });

  it('is silent and stays open when the reauth dialog is cancelled', async () => {
    const user = userEvent.setup();
    const { ReauthCancelledError } = await import('../../auth/use-step-up');
    requireStepUp.mockRejectedValueOnce(new ReauthCancelledError());
    renderDialog();
    await openDialog();

    await typePhraseAndWaitForEnable(user);
    await user.click(dialogConfirmButton());

    await waitFor(() => expect(requireStepUp).toHaveBeenCalledOnce());
    expect(toastSuccess).not.toHaveBeenCalled();
    expect(toastError).not.toHaveBeenCalled();
    expect(screen.getByText(/erase your account permanently/i)).toBeInTheDocument();
  });

  it('resets the typed confirmation text when the dialog is cancelled', async () => {
    const user = userEvent.setup();
    renderDialog();
    await openDialog();

    await user.type(screen.getByLabelText(/type "erase" to confirm/i), 'ERASE');

    await user.click(screen.getByRole('button', { name: /^cancel$/i }));
    await waitFor(() => expect(screen.queryByText(/erase your account permanently/i)).not.toBeInTheDocument());

    await openDialog();
    expect(screen.getByLabelText(/type "erase" to confirm/i)).toHaveValue('');
  });
});
