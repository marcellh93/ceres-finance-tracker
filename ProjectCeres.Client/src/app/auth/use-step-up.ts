import { useCallback } from 'react';
import { ReauthRequiredError } from '../lib/api-client';

export type UseStepUpResult = {
  /**
   * Run an async action; if it throws ReauthRequiredError, the reauth
   * dialog opens and the action is retried after success. In Phase 1
   * the dialog isn't mounted yet, so ReauthRequiredError just rethrows
   * for the caller to surface — the hook's signature is forward-
   * compatible so Phase 4 only has to wire the dialog state without
   * changing the call sites.
   */
  requireStepUp: <T>(action: () => Promise<T>) => Promise<T>;
};

export function useStepUp(): UseStepUpResult {
  const requireStepUp = useCallback(async <T,>(action: () => Promise<T>): Promise<T> => {
    try {
      return await action();
    } catch (err) {
      if (err instanceof ReauthRequiredError) {
        // Phase 4 wires this to open the modal + retry.
        throw err;
      }
      throw err;
    }
  }, []);
  return { requireStepUp };
}
