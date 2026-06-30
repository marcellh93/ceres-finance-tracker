// use-step-up.test.tsx — render a component using requireStepUp inside StepUpProvider.
import { render, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, it, expect, vi } from 'vitest';
import { StepUpProvider } from './StepUpProvider';
import { useStepUp, ReauthCancelledError } from './use-step-up';
import { ReauthRequiredError } from '../lib/api-client';

vi.mock('./auth-context', () => ({ useAuth: () => ({ user: { twoFactorEnabled: false } }) }));

function Probe({ action }: { action: () => Promise<string> }) {
  const { requireStepUp } = useStepUp();
  return <button onClick={async () => { (window as any).result = await requireStepUp(action); }}>go</button>;
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
  await waitFor(() => expect((window as any).result).toBe('done'));
  expect(action).toHaveBeenCalledTimes(2);
});

it('does not replay if the action did not throw ReauthRequiredError', async () => {
  const action = vi.fn().mockResolvedValue('immediate');
  render(<StepUpProvider><Probe action={action} /></StepUpProvider>);
  await userEvent.click(screen.getByText('go'));
  await waitFor(() => expect((window as any).result).toBe('immediate'));
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
            (window as any).cancelOutcome = 'resolved';
          } catch (e) {
            (window as any).cancelOutcome = e instanceof ReauthCancelledError ? 'cancelled' : 'other';
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
  await waitFor(() => expect((window as any).cancelOutcome).toBe('cancelled'));
  // action was attempted once (threw REAUTH_REQUIRED), never replayed
  expect(action).toHaveBeenCalledTimes(1);
});
