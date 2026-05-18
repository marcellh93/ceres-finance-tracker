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
import { apiFetch, ReauthRequiredError } from '../../lib/api-client';
import { useAuth } from '../../auth/auth-context';
import { TotpEnrollStep2BackupCodes } from './TotpEnrollStep2BackupCodes';

type Props = {
  onReauthRequired: () => void;
};

type State =
  | { kind: 'idle' }
  | { kind: 'confirming' }
  | { kind: 'pending' }
  | { kind: 'showCodes'; codes: string[] };

export function RegenerateBackupCodesDialog({ onReauthRequired }: Props) {
  const { t } = useTranslation();
  const { user } = useAuth();
  const [state, setState] = useState<State>({ kind: 'idle' });

  const onConfirm = async () => {
    setState({ kind: 'pending' });
    try {
      const result = await apiFetch<{ backupCodes: string[] }>(
        '/api/auth/mfa/backup-codes/regenerate',
        { method: 'POST', body: {} },
      );
      if (result.ok && result.data?.backupCodes) {
        setState({ kind: 'showCodes', codes: result.data.backupCodes });
        return;
      }
      // Failure: drop back to idle. Toast surfaces the failure.
      setState({ kind: 'idle' });
    } catch (err) {
      if (err instanceof ReauthRequiredError) {
        // Reset our own dialog state BEFORE notifying the parent, so the
        // dialog actually closes. Previous bug: we left state='pending'
        // (dialog still open) and only told the parent to render the
        // reauth alert — the user saw both surfaces stacked.
        setState({ kind: 'idle' });
        onReauthRequired();
        return;
      }
      setState({ kind: 'idle' });
    }
  };

  if (state.kind === 'showCodes') {
    return (
      <div className="space-y-3">
        <h3 className="text-base font-medium">{t('security.totp.regenerate.successHeading')}</h3>
        <p className="text-sm text-muted-foreground">{t('security.totp.regenerate.successBody')}</p>
        <TotpEnrollStep2BackupCodes
          codes={state.codes}
          userEmail={user?.email ?? ''}
          onDone={() => setState({ kind: 'idle' })}
          onCancelWithoutSaving={() => setState({ kind: 'idle' })}
        />
      </div>
    );
  }

  return (
    <AlertDialog
      open={state.kind === 'confirming' || state.kind === 'pending'}
      onOpenChange={(open) => {
        // Close in any open-state (confirming OR pending). Pre-fix this only
        // dropped to idle from 'confirming', so Cancel was a no-op while the
        // dialog was waiting on a POST that had already returned an error
        // (e.g. REAUTH_REQUIRED) — the dialog stayed stuck open. Ignoring
        // the close while pending isn't useful: the user CAN cancel a
        // network call by closing the dialog, and the AlertDialogAction is
        // already disabled during pending so they can't double-submit.
        if (!open && (state.kind === 'confirming' || state.kind === 'pending')) {
          setState({ kind: 'idle' });
        }
      }}
    >
      <Button type="button" variant="outline" onClick={() => setState({ kind: 'confirming' })}>
        {t('security.totp.regenerateButton')}
      </Button>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{t('security.totp.regenerate.dialogTitle')}</AlertDialogTitle>
          <AlertDialogDescription>{t('security.totp.regenerate.dialogBody')}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{t('security.totp.regenerate.dialogCancel')}</AlertDialogCancel>
          <AlertDialogAction onClick={onConfirm} disabled={state.kind === 'pending'}>
            {t('security.totp.regenerate.dialogConfirm')}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
