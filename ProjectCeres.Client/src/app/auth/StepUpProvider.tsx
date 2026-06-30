/* eslint-disable react-refresh/only-export-components -- Why: exports both StepUpProvider (component) and useStepUpContext (hook); splitting would require a separate context file with no consumer benefit. */
import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from 'react';
import { ReauthenticationDialog } from './ReauthenticationDialog';
import { ReauthCancelledError } from './use-step-up';

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
