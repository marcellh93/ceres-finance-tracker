import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { useDocumentTitle } from '../lib/use-document-title';
import { apiFetch } from '../lib/api-client';
import { useStepUp, ReauthCancelledError } from '../auth/use-step-up';
import { useAuth } from '../auth/auth-context';
import { TotpEnrollmentWizard } from '../features/security/TotpEnrollmentWizard';
import { RegenerateBackupCodesDialog } from '../features/security/RegenerateBackupCodesDialog';
import { EmailAddressSection } from '../features/security/EmailAddressSection';

type EnrollmentStart =
  | { kind: 'idle' }
  | { kind: 'starting' }
  | { kind: 'inWizard'; otpAuthUri: string; manualEntryKey: string };

export function Security() {
  const { t } = useTranslation();
  useDocumentTitle(t('security.title'));
  const { user, refresh } = useAuth();
  const [enrollment, setEnrollment] = useState<EnrollmentStart>({ kind: 'idle' });
  const { requireStepUp } = useStepUp();
  const [disableDialogOpen, setDisableDialogOpen] = useState(false);

  const startEnrollment = async () => {
    if (enrollment.kind === 'starting') return;
    setEnrollment({ kind: 'starting' });
    try {
      // Stage 12.9: reauth-gated. requireStepUp opens the password dialog on
      // 401 REAUTH_REQUIRED and replays this call, instead of the old dead end
      // that told the user to sign out and back in.
      const result = await requireStepUp(() =>
        apiFetch<{ otpAuthUri: string; manualEntryKey: string }>(
          '/api/auth/mfa/enroll',
          { method: 'POST', body: {} },
        ),
      );
      if (result.ok && result.data) {
        setEnrollment({
          kind: 'inWizard',
          otpAuthUri: result.data.otpAuthUri,
          manualEntryKey: result.data.manualEntryKey,
        });
        return;
      }
      if (!result.ok && result.code === 'MFA_ALREADY_ENROLLED') {
        // Race condition — user was already enrolled. Refresh and bail out
        // to the Enabled state.
        await refresh();
        setEnrollment({ kind: 'idle' });
        return;
      }
      // A non-ALREADY_ENROLLED failure must not look like a no-op button.
      toast.error(t('security.totp.enrollFailed'));
      setEnrollment({ kind: 'idle' });
    } catch (err) {
      // Cancelling the password prompt is a choice, not an error.
      if (!(err instanceof ReauthCancelledError)) {
        toast.error(t('security.totp.enrollFailed'));
      }
      setEnrollment({ kind: 'idle' });
    }
  };

  const disable = async () => {
    setDisableDialogOpen(false);
    try {
      const result = await requireStepUp(() =>
        apiFetch('/api/auth/mfa/disable', { method: 'POST', body: {} }),
      );
      if (result.ok) {
        await refresh();
        return;
      }
      // 409 MFA_NOT_ENABLED — auth refresh will catch any race; silently drop.
      await refresh();
    } catch (err) {
      if (!(err instanceof ReauthCancelledError)) {
        toast.error(t('security.totp.disableFailed'));
      }
    }
  };

  if (enrollment.kind === 'inWizard') {
    return (
      <div className="space-y-6">
        <h1 className="text-xl font-semibold tracking-tight">{t('security.title')}</h1>
        <TotpEnrollmentWizard
          otpAuthUri={enrollment.otpAuthUri}
          manualEntryKey={enrollment.manualEntryKey}
          onExit={async () => {
            await refresh();
            setEnrollment({ kind: 'idle' });
          }}
          onCancelWithoutSavingCodes={async () => {
            await refresh();
            setEnrollment({ kind: 'idle' });
          }}
        />
      </div>
    );
  }

  const enabled = user?.twoFactorEnabled === true;

  return (
    <div className="space-y-6">
      <h1 className="text-xl font-semibold tracking-tight">{t('security.title')}</h1>

      <EmailAddressSection />

      <section className="space-y-3 rounded-lg border border-border bg-card p-5">
        <h2 className="text-base font-medium">
          {enabled ? t('security.totp.enabledHeading') : t('security.totp.disabledHeading')}
        </h2>
        <p className="text-sm text-muted-foreground">
          {enabled ? t('security.totp.enabledBody') : t('security.totp.disabledBody')}
        </p>

        {enabled ? (
          <div className="flex flex-wrap gap-3">
            <RegenerateBackupCodesDialog />
            <Button type="button" variant="outline" onClick={() => setDisableDialogOpen(true)}>
              {t('security.totp.disableButton')}
            </Button>
          </div>
        ) : (
          <Button type="button" onClick={startEnrollment} disabled={enrollment.kind === 'starting'}>
            {t('security.totp.enableButton')}
          </Button>
        )}
      </section>

      <AlertDialog open={disableDialogOpen} onOpenChange={setDisableDialogOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('security.totp.disable.dialogTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('security.totp.disable.dialogBody')}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('security.totp.disable.dialogCancel')}</AlertDialogCancel>
            <AlertDialogAction onClick={disable}>
              {t('security.totp.disable.dialogConfirm')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
