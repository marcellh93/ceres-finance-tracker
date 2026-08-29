import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { useAuth } from '../../auth/auth-context';
import { SecurityWizardStepper, type SecurityWizardStep } from './SecurityWizardStepper';
import { TotpEnrollStep1ScanVerify } from './TotpEnrollStep1ScanVerify';
import { TotpEnrollStep2BackupCodes } from './TotpEnrollStep2BackupCodes';
import { TotpEnrollStep3Done } from './TotpEnrollStep3Done';

type Props = {
  otpAuthUri: string;
  manualEntryKey: string;
  /** Called when the wizard exits (any path — done, cancelled, or restart). */
  onExit: () => void;
  /** Called when the user enrolled but cancelled out of the backup-codes step. */
  onCancelWithoutSavingCodes: () => void;
};

export function TotpEnrollmentWizard({
  otpAuthUri,
  manualEntryKey,
  onExit,
  onCancelWithoutSavingCodes,
}: Props) {
  const { t } = useTranslation();
  const { user, refresh } = useAuth();
  const [step, setStep] = useState<SecurityWizardStep>(1);
  const [backupCodes, setBackupCodes] = useState<string[]>([]);

  const handleEnrolled = async (codes: string[]) => {
    setBackupCodes(codes);
    setStep(2);
  };

  const handleDone = () => setStep(3);

  const handleBack = async () => {
    await refresh();
    onExit();
  };

  const handleCancelWithoutSaving = async () => {
    await refresh();
    onCancelWithoutSavingCodes();
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <SecurityWizardStepper currentStep={step} />
        {step === 1 ? (
          <Button type="button" variant="link" onClick={onExit} className="px-0">
            {t('security.totp.wizard.cancel')}
          </Button>
        ) : null}
      </div>

      {step === 1 && (
        <TotpEnrollStep1ScanVerify
          otpAuthUri={otpAuthUri}
          manualEntryKey={manualEntryKey}
          onEnrolled={handleEnrolled}
          onRestart={onExit}
        />
      )}
      {step === 2 && (
        <TotpEnrollStep2BackupCodes
          codes={backupCodes}
          userEmail={user?.email ?? ''}
          onDone={handleDone}
          onCancelWithoutSaving={handleCancelWithoutSaving}
        />
      )}
      {step === 3 && <TotpEnrollStep3Done onBack={handleBack} />}
    </div>
  );
}
