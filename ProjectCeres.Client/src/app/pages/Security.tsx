import { useState } from 'react';
import { useTranslation } from 'react-i18next';
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
import { apiFetch, ReauthRequiredError } from '../lib/api-client';
import { useAuth } from '../auth/auth-context';
import { TotpEnrollmentWizard } from '../features/security/TotpEnrollmentWizard';
import { RegenerateBackupCodesDialog } from '../features/security/RegenerateBackupCodesDialog';

type EnrollmentStart =
  | { kind: 'idle' }
  | { kind: 'starting' }
  | { kind: 'inWizard'; otpAuthUri: string; manualEntryKey: string };

export function Security() {
  const { t } = useTranslation();
  useDocumentTitle(t('security.title'));
  const { user, refresh, logout } = useAuth();
  const [enrollment, setEnrollment] = useState<EnrollmentStart>({ kind: 'idle' });
  const [reauthRequired, setReauthRequired] = useState(false);
  const [disableDialogOpen, setDisableDialogOpen] = useState(false);

  const handleSignOut = async () => {
    await logout();
    window.location.href = '/login';
  };

  const startEnrollment = async () => {
    if (enrollment.kind === 'starting') return;
    setEnrollment({ kind: 'starting' });
    setReauthRequired(false);
    try {
      const result = await apiFetch<{ otpAuthUri: string; manualEntryKey: string }>(
        '/api/auth/mfa/enroll',
        { method: 'POST', body: {} },
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
      setEnrollment({ kind: 'idle' });
    } catch (err) {
      if (err instanceof ReauthRequiredError) {
        setReauthRequired(true);
        setEnrollment({ kind: 'idle' });
        return;
      }
      setEnrollment({ kind: 'idle' });
    }
  };

  const disable = async () => {
    setDisableDialogOpen(false);
    try {
      const result = await apiFetch('/api/auth/mfa/disable', { method: 'POST', body: {} });
      if (result.ok) {
        await refresh();
        return;
      }
      // 409 MFA_NOT_ENABLED — auth refresh will catch any race; silently drop.
      await refresh();
    } catch (err) {
      if (err instanceof ReauthRequiredError) {
        setReauthRequired(true);
        return;
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
          onReauthRequired={() => {
            setReauthRequired(true);
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

      <section className="space-y-3 rounded-lg border border-border bg-card p-5">
        <h2 className="text-base font-medium">
          {enabled ? t('security.totp.enabledHeading') : t('security.totp.disabledHeading')}
        </h2>
        <p className="text-sm text-muted-foreground">
          {enabled ? t('security.totp.enabledBody') : t('security.totp.disabledBody')}
        </p>

        {reauthRequired && (
          <div role="alert" className="rounded-md border border-destructive/40 bg-destructive/10 p-3 text-sm">
            <p className="text-destructive">{t('security.totp.reauthRequired')}</p>
            <Button type="button" variant="link" onClick={handleSignOut} className="mt-1 px-0">
              {t('security.totp.signOutLink')}
            </Button>
          </div>
        )}

        {enabled ? (
          <div className="flex flex-wrap gap-3">
            <RegenerateBackupCodesDialog onReauthRequired={() => setReauthRequired(true)} />
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
