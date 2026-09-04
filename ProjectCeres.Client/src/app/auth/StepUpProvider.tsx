/* eslint-disable react-refresh/only-export-components -- Why: exports both StepUpProvider (component) and useStepUpContext (hook); splitting would require a separate context file with no consumer benefit. */
import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from 'react';
import { ReauthenticationDialog } from './ReauthenticationDialog';
import { ReauthCancelledError, ReauthBusyError } from './use-step-up';

type StepUpContextValue = {
  openStepUp: () => Promise<void>;
};

const StepUpContext = createContext<StepUpContextValue | null>(null);

export function useStepUpContext(): StepUpContextValue {
  const ctx = useContext(StepUpContext);
  if (!ctx) throw new Error('useStepUpContext must be used inside <StepUpProvider>');
  return ctx;
}

export function StepUpProvider({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  // Refs hold the resolve/reject for the current pending openStepUp() call.
  const resolveRef = useRef<(() => void) | null>(null);
  const rejectRef = useRef<((err: unknown) => void) | null>(null);

  const openStepUp = useCallback((): Promise<void> => {
    return new Promise<void>((resolve, reject) => {
      // A second call while one is in flight used to overwrite these refs, orphaning
      // the first promise — its caller awaited forever with no dialog and no error.
      // One dialog serves one action, so reject the newcomer rather than stack or
      // silently drop. ReauthBusyError, NOT ReauthCancelledError: callers stay
      // silent on cancellation because the user chose it, and staying silent here
      // would leave a caller with no dialog, no error and no state change — which
      // is exactly what it did to the enrol-verify step before this distinction
      // existed. Reachable since Stage 12.8 put four gated actions on /app/security.
      if (resolveRef.current !== null) {
        reject(new ReauthBusyError());
        return;
      }
      resolveRef.current = resolve;
      rejectRef.current = reject;
      setOpen(true);
    });
  }, []);

  const handleSuccess = useCallback(() => {
    setOpen(false);
    resolveRef.current?.();
    resolveRef.current = null;
    rejectRef.current = null;
  }, []);

  const handleCancel = useCallback(() => {
    setOpen(false);
    rejectRef.current?.(new ReauthCancelledError('Reauth cancelled by user'));
    resolveRef.current = null;
    rejectRef.current = null;
  }, []);

  return (
    <StepUpContext.Provider value={{ openStepUp }}>
      {children}
      <ReauthenticationDialog open={open} onSuccess={handleSuccess} onCancel={handleCancel} />
    </StepUpContext.Provider>
  );
}
