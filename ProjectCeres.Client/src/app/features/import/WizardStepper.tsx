import { Check } from 'lucide-react';
import { cn } from '@/lib/utils';

export type WizardStep = 1 | 2 | 3 | 4;

const STEPS: { value: WizardStep; label: string }[] = [
  { value: 1, label: 'File' },
  { value: 2, label: 'Mapping' },
  { value: 3, label: 'Review' },
  { value: 4, label: 'Result' },
];

type Props = {
  currentStep: WizardStep;
  /** Called when a completed step pill is clicked. Steps after current never fire. */
  onJumpBack: (step: WizardStep) => void;
};

export function WizardStepper({ currentStep, onJumpBack }: Props) {
  return (
    <ol
      aria-label="Import progress"
      className="flex items-center gap-2 sm:gap-4"
    >
      {STEPS.map((step, idx) => {
        const isCurrent  = step.value === currentStep;
        const isComplete = step.value < currentStep;
        const isFuture   = step.value > currentStep;

        const pill = (
          <span
            className={cn(
              'flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-xs font-medium',
              isCurrent  ? 'bg-primary text-primary-foreground' : '',
              isComplete ? 'bg-primary/15 text-primary' : '',
              isFuture   ? 'bg-muted text-muted-foreground' : '',
            )}
          >
            {isComplete ? <Check className="h-3.5 w-3.5" aria-hidden /> : step.value}
          </span>
        );

        return (
          <li
            key={step.value}
            className="flex items-center gap-2"
            aria-current={isCurrent ? 'step' : undefined}
          >
            {isComplete ? (
              <button
                type="button"
                onClick={() => onJumpBack(step.value)}
                className="flex items-center gap-2 rounded-md focus:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
                aria-label={`Go back to step ${step.value}: ${step.label}`}
              >
                {pill}
                <span className="text-sm font-medium text-primary">{step.label}</span>
              </button>
            ) : (
              <div className="flex items-center gap-2">
                {pill}
                <span
                  className={cn(
                    'text-sm',
                    isCurrent ? 'font-medium text-foreground' : 'text-muted-foreground',
                  )}
                >
                  {step.label}
                </span>
              </div>
            )}
            {idx < STEPS.length - 1 ? (
              <span
                aria-hidden
                className={cn(
                  'hidden sm:block h-px w-8',
                  isComplete ? 'bg-primary/40' : 'bg-border',
                )}
              />
            ) : null}
          </li>
        );
      })}
    </ol>
  );
}
