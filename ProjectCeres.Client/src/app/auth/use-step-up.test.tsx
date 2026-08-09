// use-step-up.test.tsx — render a component using requireStepUp inside StepUpProvider.
import { render, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { it, expect, vi } from 'vitest';
import { StepUpProvider } from './StepUpProvider';
import { useStepUp, ReauthCancelledError } from './use-step-up';
import { ReauthRequiredError } from '../lib/api-client';

vi.mock('./auth-context', () => ({ useAuth: () => ({ user: { twoFactorEnabled: false } }) }));

// The probe components stash their outcome on window so the assertions can read
// it after an async click handler resolves.
declare global {
  interface Window {
    result?: string;
    cancelOutcome?: string;
  }
}

function Probe({ action }: { action: () => Promise<string> }) {
  const { requireStepUp } = useStepUp();
  return <button onClick={async () => { window.result = await requireStepUp(action); }}>go</button>;
}

it('replays the action after a successful reauth and returns its result', async () => {
  // First call throws REAUTH_REQUIRED; second call (the replay) succeeds.
  const action = vi.fn()
    .mockRejectedValueOnce(new ReauthRequiredError('reauth'))
    .mockResolvedValueOnce('done');
  // Mock the dialog's reauth POST to 204 so onSuccess fires.
  global.fetch = vi.fn(async (url: string) =>
    url.includes('/api/auth/csrf') ? ({ headers: new Headers({ 'X-XSRF-TOKEN': 't' }) } as unknown as Response)
                                   : ({ status: 204 } as Response));
  render(<StepUpProvider><Probe action={action} /></StepUpProvider>);
  await userEvent.click(screen.getByText('go'));
  // dialog opens; submit it
  await userEvent.type(await screen.findByLabelText(/password/i), 'pw');
  await userEvent.click(screen.getByRole('button', { name: /confirm/i }));
  await waitFor(() => expect(window.result).toBe('done'));
  expect(action).toHaveBeenCalledTimes(2);
});

it('does not replay if the action did not throw ReauthRequiredError', async () => {
  const action = vi.fn().mockResolvedValue('immediate');
  render(<StepUpProvider><Probe action={action} /></StepUpProvider>);
  await userEvent.click(screen.getByText('go'));
  await waitFor(() => expect(window.result).toBe('immediate'));
  expect(action).toHaveBeenCalledTimes(1);
});

it('propagates ReauthCancelledError and does not replay when the user cancels', async () => {
  const action = vi.fn().mockRejectedValueOnce(new ReauthRequiredError('reauth'));
  function CancelProbe() {
    const { requireStepUp } = useStepUp();
    return (
      <button
        onClick={async () => {
          try {
            await requireStepUp(action);
            window.cancelOutcome = 'resolved';
          } catch (e) {
            window.cancelOutcome = e instanceof ReauthCancelledError ? 'cancelled' : 'other';
          }
        }}
      >
        go
      </button>
    );
  }
  render(<StepUpProvider><CancelProbe /></StepUpProvider>);
  await userEvent.click(screen.getByText('go'));
  // dialog opens; cancel it
  await userEvent.click(await screen.findByRole('button', { name: /cancel/i }));
  await waitFor(() => expect(window.cancelOutcome).toBe('cancelled'));
  // action was attempted once (threw REAUTH_REQUIRED), never replayed
  expect(action).toHaveBeenCalledTimes(1);
});
