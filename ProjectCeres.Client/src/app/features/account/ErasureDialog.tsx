import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from '@/components/ui/alert-dialog';
import { Field } from '../../components/Field';
import { useStepUp, ReauthCancelledError } from '../../auth/use-step-up';
import { requestErasure } from './account-api';

const CONFIRM_PHRASE = 'ERASE';
// A short, deliberate pause between the phrase matching and the destructive
// button becoming clickable — the one moment in this dialog where color alone
// (variant="destructive") isn't enough friction differential from a routine
// confirm. Not a debounce; a beat to interrupt a reflexive click.
const CONFIRM_ENABLE_DELAY_MS = 1200;

type State = { kind: 'idle' | 'confirming' | 'pending' };

/**
 * Highest-risk action in the app: irreversibly seals the account, with a 72h
 * cancel window before a background worker executes the erasure. The typed
 * "ERASE" gate mirrors the server's own case-sensitive check
 * (ProfileApiController.RequestErasure) so a mistyped value never reaches the
 * network — the button stays disabled until the input matches exactly.
 */
export function ErasureDialog() {
  const { t } = useTranslation();
  const { requireStepUp } = useStepUp();
  const [state, setState] = useState<State>({ kind: 'idle' });
  const [confirmText, setConfirmText] = useState('');
  const [matchedRecently, setMatchedRecently] = useState(false);
  const [pending, setPending] = useState(false);

  const matches = confirmText === CONFIRM_PHRASE;

  useEffect(() => {
    if (!matches) {
      setMatchedRecently(false);
      return;
    }
    const timer = setTimeout(() => setMatchedRecently(true), CONFIRM_ENABLE_DELAY_MS);
    return () => clearTimeout(timer);
  }, [matches]);

  const reset = () => {
    setState({ kind: 'idle' });
    setConfirmText('');
    setMatchedRecently(false);
  };

  const onConfirm = async () => {
    setState({ kind: 'pending' });
    try {
      const result = await requireStepUp(() => requestErasure());
      if (result.ok) {
        toast.success(result.data?.message ?? t('account.erasure.scheduledDefault'));
        setPending(true);
        reset();
        return;
      }
      if (result.status === 429) {
        toast.error(t('account.erasure.errorRateLimited'));
        setState({ kind: 'confirming' });
        return;
      }
      if (result.status === 422) {
        // The confirm gate got out of sync with the server's own check (e.g. a
        // stale build) — say so specifically rather than a generic failure
        // message, since a true server error (5xx) deserves different copy.
        toast.error(t('account.erasure.errorOutOfSync'));
        setState({ kind: 'confirming' });
        return;
      }
      toast.error(t('account.erasure.errorGeneric'));
      setState({ kind: 'confirming' });
    } catch (err) {
      if (err instanceof ReauthCancelledError) {
        setState({ kind: 'confirming' });
        return;
      }
      toast.error(t('account.erasure.errorNetwork'));
      setState({ kind: 'confirming' });
    }
  };

  const canConfirm = matches && matchedRecently && state.kind !== 'pending';

  return (
    <div className="space-y-3">
      {pending && (
        <div
          role="status"
          className="rounded-md border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning-foreground"
        >
          {t('account.erasure.pendingBanner')}
        </div>
      )}
      <AlertDialog
        open={state.kind === 'confirming' || state.kind === 'pending'}
        onOpenChange={(open) => {
          if (!open && state.kind !== 'pending') reset();
          else if (open) setState({ kind: 'confirming' });
        }}
      >
        <AlertDialogTrigger
          render={
            <Button type="button" variant="destructive">
              {t('account.erasure.trigger')}
            </Button>
          }
        />
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('account.erasure.dialogTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('account.erasure.dialogDescription')}</AlertDialogDescription>
          </AlertDialogHeader>
          <div className="px-4 pb-2">
            <Field
              label={t('account.erasure.confirmLabel', { phrase: CONFIRM_PHRASE })}
              htmlFor="erasure-confirm"
              error={
                confirmText.length > 0 && !matches
                  ? t('account.erasure.confirmMismatch')
                  : undefined
              }
            >
              <Input
                id="erasure-confirm"
                value={confirmText}
                onChange={(e) => setConfirmText(e.target.value)}
                placeholder={CONFIRM_PHRASE}
                disabled={state.kind === 'pending'}
                autoComplete="off"
                autoCapitalize="off"
                spellCheck={false}
                aria-invalid={confirmText.length > 0 && !matches}
                aria-describedby={confirmText.length > 0 && !matches ? 'erasure-confirm-error' : undefined}
                className="max-sm:h-11"
              />
            </Field>
          </div>
          <AlertDialogFooter>
            {/* Cancel gets the size bump, not Confirm — the safe path should be
                the visually easier target for a rushed or emotional click,
                not the irreversible one. Confirm stays default-sized; its
                own friction comes from the typed gate + enable delay above. */}
            <AlertDialogCancel size="lg">{t('account.erasure.cancel')}</AlertDialogCancel>
            <AlertDialogAction
              variant="destructive"
              onClick={() => void onConfirm()}
              disabled={!canConfirm}
            >
              {state.kind === 'pending' ? t('account.erasure.confirmPending') : t('account.erasure.trigger')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
