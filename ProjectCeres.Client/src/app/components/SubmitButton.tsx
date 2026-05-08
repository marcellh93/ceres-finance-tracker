import { AlertCircle, Check, Loader2 } from 'lucide-react';
import { useState, type ComponentProps } from 'react';
import { Button } from '@/components/ui/button';

type ButtonProps = ComponentProps<typeof Button>;

type Phase = 'idle' | 'loading' | 'success' | 'error';

type SubmitButtonProps = Omit<ButtonProps, 'onClick' | 'type'> & {
  onClick: () => Promise<void>;
  loadingLabel?: string;
  successLabel?: string;
  errorLabel?: string;
  successDuration?: number;
  children: React.ReactNode;
};

const sleep = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

export function SubmitButton({
  onClick,
  loadingLabel = 'Saving…',
  successLabel = 'Saved',
  errorLabel = 'Try again',
  successDuration = 800,
  children,
  variant,
  disabled,
  ...rest
}: SubmitButtonProps) {
  const [phase, setPhase] = useState<Phase>('idle');

  async function handleClick() {
    if (phase === 'loading' || phase === 'success') return;
    setPhase('loading');
    try {
      await onClick();
      setPhase('success');
      await sleep(successDuration);
      setPhase('idle');
    } catch {
      setPhase('error');
    }
  }

  const isBusy = phase === 'loading' || phase === 'success';
  const resolvedVariant = phase === 'error' ? 'destructive' : variant;

  return (
    <Button
      {...rest}
      type="button"
      variant={resolvedVariant}
      disabled={isBusy || disabled}
      aria-busy={phase === 'loading' ? 'true' : undefined}
      aria-live="polite"
      onClick={handleClick}
    >
      {phase === 'loading' && (
        <>
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
          {loadingLabel}
        </>
      )}
      {phase === 'success' && (
        <>
          <Check className="h-4 w-4" aria-hidden="true" />
          {successLabel}
        </>
      )}
      {phase === 'error' && (
        <>
          <AlertCircle className="h-4 w-4" aria-hidden="true" />
          {errorLabel}
        </>
      )}
      {phase === 'idle' && children}
    </Button>
  );
}
