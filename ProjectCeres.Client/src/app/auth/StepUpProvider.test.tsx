import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { I18nextProvider } from 'react-i18next';
import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest';
import i18n from '../i18n/i18n';
import { StepUpProvider } from './StepUpProvider';
import { AuthProvider } from './auth-context';
import { MemoryRouter } from 'react-router-dom';
import { useStepUp, ReauthCancelledError, ReauthBusyError } from './use-step-up';
import { ReauthRequiredError } from '../lib/api-client';

declare global {
  interface Window {
    outcomes: string[];
  }
}

/**
 * Harness that fires two reauth-gated actions back to back, the way a user can on
 * /app/security — which since Stage 12.8 carries four gated actions on one screen.
 */
function TwoActions() {
  const { requireStepUp } = useStepUp();
  return (
    <button
      onClick={() => {
        window.outcomes = [];
        const gated = () => Promise.reject(new ReauthRequiredError('reauth'));
        // Deliberately NOT awaited in sequence — both are started before either settles.
        const label = (e: unknown, n: number) =>
          e instanceof ReauthBusyError ? `busy-${n}`
          : e instanceof ReauthCancelledError ? `cancelled-${n}`
          : `other-${n}`;
        void requireStepUp(gated).catch((e) => window.outcomes.push(label(e, 1)));
        void requireStepUp(gated).catch((e) => window.outcomes.push(label(e, 2)));
      }}
    >
      go
    </button>
  );
}

describe('StepUpProvider — concurrent step-up guard', () => {
  // AuthProvider fires GET /api/auth/me on mount. Unstubbed, jsdom cannot resolve
  // the relative URL and the rejection escapes the test as an unhandled error —
  // vitest still prints "passed" but exits 1. Caught by build-matrix, not by
  // reading the summary line.
  beforeEach(() => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async () =>
      new Response(null, { status: 401 }),
    );
  });
  afterEach(() => vi.restoreAllMocks());

  it('rejects a second prompt instead of orphaning the first', async () => {
    // Before the guard, the second openStepUp() overwrote the first call's resolve
    // and reject refs. The first promise then never settled: its caller awaited
    // forever, with no dialog and no error — a permanent hang, not a visible failure.
    // The spec named this case and the test for it; neither existed until now.
    const user = userEvent.setup();
    render(
      <I18nextProvider i18n={i18n}>
        <MemoryRouter>
          <AuthProvider>
            <StepUpProvider>
              <TwoActions />
            </StepUpProvider>
          </AuthProvider>
        </MemoryRouter>
      </I18nextProvider>,
    );

    await user.click(screen.getByRole('button', { name: 'go' }));

    // The second call must settle — that is the whole point. If the guard is
    // removed this assertion times out rather than failing fast, which is exactly
    // the user-visible symptom it defends against.
    // The rejection must NOT be a ReauthCancelledError. Callers treat cancellation
    // as a user choice and stay silent — which left the enrol-verify screen dead
    // when the guard used that type. 'busy-2' proves the distinction survives.
    await waitFor(() => expect(window.outcomes).toContain('busy-2'));

    // And exactly one dialog is on screen, not two stacked.
    await waitFor(() => expect(screen.getAllByRole('dialog')).toHaveLength(1));
  });

  it('releases the guard after the dialog closes, so later prompts still open', async () => {
    // The guard keys on a non-null resolveRef; both the success and cancel paths
    // clear it. If either stopped clearing, every future reauth in the session would
    // be rejected on sight — a worse failure than the one being fixed.
    const user = userEvent.setup();
    const onCancelled = vi.fn();

    function OneAction() {
      const { requireStepUp } = useStepUp();
      return (
        <button
          onClick={() =>
            void requireStepUp(() => Promise.reject(new ReauthRequiredError('reauth'))).catch(
              onCancelled,
            )
          }
        >
          go
        </button>
      );
    }

    render(
      <I18nextProvider i18n={i18n}>
        <MemoryRouter>
          <AuthProvider>
            <StepUpProvider>
              <OneAction />
            </StepUpProvider>
          </AuthProvider>
        </MemoryRouter>
      </I18nextProvider>,
    );

    await user.click(screen.getByRole('button', { name: 'go' }));
    await screen.findByRole('dialog');
    await user.keyboard('{Escape}');
    await waitFor(() => expect(onCancelled).toHaveBeenCalledTimes(1));

    // Second attempt after the first closed: the dialog must open again.
    await user.click(screen.getByRole('button', { name: 'go' }));
    await waitFor(() => expect(screen.getByRole('dialog')).toBeInTheDocument());
  });
});
