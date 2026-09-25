import { render, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AccountPage } from './AccountPage';

const { toastError, toastSuccess } = vi.hoisted(() => ({
  toastError: vi.fn(),
  toastSuccess: vi.fn(),
}));
vi.mock('sonner', () => ({ toast: { error: toastError, success: toastSuccess } }));

// Pass-through step-up: the page's job is to POST and toast; reauth routing is
// exercised in use-step-up's own suite.
const requireStepUp = vi.fn(<T,>(action: () => Promise<T>) => action());
vi.mock('../../auth/use-step-up', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../auth/use-step-up')>()),
  useStepUp: () => ({ requireStepUp }),
}));

const requestDataExport = vi.fn();
vi.mock('./account-api', () => ({
  EXPORT_URL: '/api/profile/export',
  requestDataExport: () => requestDataExport(),
}));

function renderPage() {
  return render(
    <MemoryRouter>
      <AccountPage />
    </MemoryRouter>,
  );
}

describe('AccountPage', () => {
  beforeEach(() => vi.clearAllMocks());

  it('shows the export action', () => {
    renderPage();
    expect(screen.getByRole('button', { name: /export my data/i })).toBeInTheDocument();
  });

  it('requests an export and shows a success toast on 202', async () => {
    requestDataExport.mockResolvedValue({ ok: true, status: 202, data: { jobId: 'j1', message: 'ok' } });
    renderPage();

    await userEvent.click(screen.getByRole('button', { name: /export my data/i }));

    await waitFor(() => expect(requestDataExport).toHaveBeenCalledOnce());
    expect(toastSuccess).toHaveBeenCalledWith(expect.stringContaining('email you'));
    expect(toastError).not.toHaveBeenCalled();
  });

  it('shows the daily-limit toast on 429', async () => {
    requestDataExport.mockResolvedValue({ ok: false, status: 429, code: 'RATE_LIMITED', message: '' });
    renderPage();

    await userEvent.click(screen.getByRole('button', { name: /export my data/i }));

    await waitFor(() => expect(toastError).toHaveBeenCalledWith(expect.stringContaining('one export per day')));
    expect(toastSuccess).not.toHaveBeenCalled();
  });

  it('shows a generic error toast on other failures', async () => {
    requestDataExport.mockResolvedValue({ ok: false, status: 500, code: 'ERROR', message: '' });
    renderPage();

    await userEvent.click(screen.getByRole('button', { name: /export my data/i }));

    await waitFor(() => expect(toastError).toHaveBeenCalledWith(expect.stringContaining('try again')));
  });

  it('is silent when the reauth dialog is cancelled', async () => {
    const { ReauthCancelledError } = await import('../../auth/use-step-up');
    requireStepUp.mockRejectedValueOnce(new ReauthCancelledError());
    renderPage();

    await userEvent.click(screen.getByRole('button', { name: /export my data/i }));

    await waitFor(() => expect(requireStepUp).toHaveBeenCalledOnce());
    expect(toastSuccess).not.toHaveBeenCalled();
    expect(toastError).not.toHaveBeenCalled();
  });
});
