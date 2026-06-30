import { useCallback } from 'react';
import { ReauthRequiredError } from '../lib/api-client';
import { useStepUpContext } from './StepUpProvider';

export class ReauthCancelledError extends Error {
  constructor(message = 'Reauth cancelled') {
    super(message);
    this.name = 'ReauthCancelledError';
  }
}

export type UseStepUpResult = {
  /**
   * Run an async action; if it throws ReauthRequiredError, the reauth
   * dialog opens and the action is retried once after success. Cancel
   * rejects with ReauthCancelledError. A second ReauthRequiredError from
   * the replay propagates to the caller without re-opening the dialog.
   */
  requireStepUp: <T>(action: () => Promise<T>) => Promise<T>;
};

export function useStepUp(): UseStepUpResult {
  const { openStepUp } = useStepUpContext();

  const requireStepUp = useCallback(async <T,>(action: () => Promise<T>): Promise<T> => {
    try {
      return await action();
    } catch (err) {
      if (err instanceof ReauthRequiredError) {
        await openStepUp(); // opens dialog, resolves on 204, rejects (ReauthCancelledError) on cancel
        return await action(); // auto-replay; a second ReauthRequiredError propagates (no loop)
      }
      throw err;
    }
  }, [openStepUp]);

  return { requireStepUp };
}
