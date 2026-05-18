import { Check } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/utils';

/**
 * Stage 9.6 — 3-step stepper for the TOTP enrolment wizard.
 * Local to the security feature; Import's WizardStepper is hard-coded
 * to 4 Import-specific labels so it can't be reused here.
 */
export type SecurityWizardStep = 1 | 2 | 3;

type Props = {
  currentStep: SecurityWizardStep;
};

export function SecurityWizardStepper({ currentStep }: Props) {
  const { t } = useTranslation();
  const steps: { value: SecurityWizardStep; label: string }[] = [
    { value: 1, label: t('security.totp.wizard.stepper.scanVerify') },
    { value: 2, label: t('security.totp.wizard.stepper.saveCodes') },
    { value: 3, label: t('security.totp.wizard.stepper.done') },
  ];

  return (
    <ol aria-label="Two-factor setup progress" className="flex items-center gap-2 sm:gap-4">
      {steps.map((step, idx) => {
        const isCurrent = step.value === currentStep;
        const isComplete = step.value < currentStep;
        const isFuture = step.value > currentStep;

        return (
          <li
            key={step.value}
            className="flex items-center gap-2"
            aria-current={isCurrent ? 'step' : undefined}
          >
            <span
              className={cn(
                'flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-xs font-medium',
                isCurrent && 'bg-primary text-primary-foreground',
                isComplete && 'bg-primary/15 text-primary',
                isFuture && 'bg-muted text-muted-foreground',
              )}
            >
              {isComplete ? <Check className="h-3.5 w-3.5" aria-hidden /> : step.value}
            </span>
            <span
              className={cn(
                'text-sm',
                isCurrent ? 'font-medium text-foreground' : 'text-muted-foreground',
              )}
            >
              {step.label}
            </span>
            {idx < steps.length - 1 ? (
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
