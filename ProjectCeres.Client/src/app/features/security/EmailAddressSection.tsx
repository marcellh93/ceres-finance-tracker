import { useCallback, useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { AlertTriangle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Field } from '../../components/Field';
import { apiFetch } from '../../lib/api-client';
import { useAuth } from '../../auth/auth-context';
import { useStepUp, ReauthCancelledError } from '../../auth/use-step-up';

const schema = z.object({
  newEmail: z.string().email('security.email.errors.invalidEmail'),
});
type FormValues = z.infer<typeof schema>;

type PendingDto = {
  pending: boolean;
  maskedEmail: string | null;
  expiresAt: string | null;
  expired: boolean;
};

type Mode =
  | { kind: 'idle' }
  | { kind: 'form' }
  | { kind: 'sent'; email: string };

/**
 * Minutes remaining until `expiresAt`, ticking once a minute so a user who leaves
 * the page open does not read a stale number. Returns 0 once the window closes.
 */
function useMinutesRemaining(expiresAt: string | null): number | null {
  const compute = useCallback(() => {
    if (!expiresAt) return null;
    const ms = new Date(expiresAt).getTime() - Date.now();
    return ms <= 0 ? 0 : Math.ceil(ms / 60_000);
  }, [expiresAt]);

  const [minutes, setMinutes] = useState<number | null>(compute);

  useEffect(() => {
    setMinutes(compute());
    if (!expiresAt) return;
    // 30s rather than 60s: a minute-granular label drifts by up to a full minute
    // if we only sample every minute, and the number matters most near zero.
    const id = window.setInterval(() => setMinutes(compute()), 30_000);
    return () => window.clearInterval(id);
  }, [compute, expiresAt]);

  return minutes;
}

export function EmailAddressSection() {
  const { t } = useTranslation();
  const { user } = useAuth();
  const { requireStepUp } = useStepUp();
  const [mode, setMode] = useState<Mode>({ kind: 'idle' });
  const [pending, setPending] = useState<PendingDto | null>(null);
  const [formError, setFormError] = useState<string | null>(null);

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { newEmail: '' },
  });

  const loadPending = useCallback(async () => {
    try {
      const result = await apiFetch<PendingDto>('/api/auth/email-change/pending');
      if (result.ok && result.data) setPending(result.data);
    } catch {
      // A failed read leaves the banner hidden rather than showing a wrong state.
      // The section still works: the change form is the primary affordance.
    }
  }, []);

  useEffect(() => {
    void loadPending();
  }, [loadPending]);

  const minutes = useMinutesRemaining(
    pending?.pending && !pending.expired ? pending.expiresAt : null,
  );

  const onSubmit = async (values: FormValues) => {
    setFormError(null);
    try {
      // Reauth-gated on the server ([RequireRecentAuth]); requireStepUp opens the
      // password dialog on 401 REAUTH_REQUIRED and replays this call after success.
      const result = await requireStepUp(() =>
        apiFetch('/api/auth/email-change/request', {
          method: 'POST',
          body: { newEmail: values.newEmail },
        }),
      );

      if (result.ok) {
        setMode({ kind: 'sent', email: values.newEmail });
        form.reset();
        void loadPending();
        return;
      }

      const byCode: Record<string, string> = {
        EMAIL_UNCHANGED: 'security.email.errors.unchanged',
        EMAIL_ALREADY_IN_USE: 'security.email.errors.alreadyInUse',
        RATE_LIMITED: 'security.email.errors.rateLimited',
      };
      setFormError(t(byCode[result.code ?? ''] ?? 'security.email.errors.network'));
    } catch (err) {
      // Cancelling the password prompt is a deliberate choice, not a failure —
      // showing an error for it would be scolding the user for changing their mind.
      if (err instanceof ReauthCancelledError) return;
      setFormError(t('security.email.errors.network'));
    }
  };

  const showBanner = pending?.pending === true;

  return (
    <section className="space-y-3 rounded-lg border border-border bg-card p-5">
      <h2 className="text-base font-medium">{t('security.email.heading')}</h2>

      <p className="text-sm text-muted-foreground">
        {t('security.email.current')}: <span className="text-foreground">{user?.email}</span>
      </p>

      {showBanner && (
        <div
          role="status"
          aria-live="polite"
          className="flex items-start gap-3 rounded-md border border-warning/30 bg-warning/10 p-4 text-sm"
        >
          <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-warning" aria-hidden />
          <div className="flex-1 space-y-2">
            {pending.expired ? (
              <>
                <p className="font-medium">{t('security.email.expiredHeading')}</p>
                <p>{t('security.email.expiredBody', { email: pending.maskedEmail })}</p>
              </>
            ) : (
              <>
                <p className="font-medium">{t('security.email.pendingHeading')}</p>
                <p>{t('security.email.pendingBody', { email: pending.maskedEmail })}</p>
                <p className="text-muted-foreground">
                  {minutes !== null && minutes > 0
                    ? t('security.email.pendingExpiresIn', { count: minutes })
                    : t('security.email.pendingExpiresSoon')}
                </p>
              </>
            )}
            {mode.kind === 'idle' && (
              <Button type="button" variant="outline" size="sm" onClick={() => setMode({ kind: 'form' })}>
                {t(pending.expired ? 'security.email.resend' : 'security.email.changeButton')}
              </Button>
            )}
          </div>
        </div>
      )}

      {mode.kind === 'sent' && (
        <div className="space-y-1 text-sm" role="status" aria-live="polite">
          <p className="font-medium">{t('security.email.sentTitle')}</p>
          {/* Full address here, not masked: they typed it seconds ago, and confirming
              what they typed is the entire value of this message. */}
          <p className="text-muted-foreground">
            {t('security.email.sentBody', { email: mode.email })}
          </p>
          <p className="text-muted-foreground">{t('security.email.sentNotice')}</p>
        </div>
      )}

      {mode.kind === 'idle' && !showBanner && (
        <Button type="button" onClick={() => setMode({ kind: 'form' })}>
          {t('security.email.changeButton')}
        </Button>
      )}

      {mode.kind === 'form' && (
        <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-3" noValidate>
          <Field
            label={t('security.email.newLabel')}
            htmlFor="new-email"
            error={
              form.formState.errors.newEmail?.message
                ? t(form.formState.errors.newEmail.message)
                : undefined
            }
          >
            <Input
              id="new-email"
              type="email"
              autoComplete="email"
              autoFocus
              {...form.register('newEmail')}
            />
          </Field>

          {formError && (
            <div role="alert" aria-live="polite" className="text-sm text-destructive">
              {formError}
            </div>
          )}

          <div className="flex flex-wrap gap-3">
            <Button type="submit" disabled={form.formState.isSubmitting}>
              {form.formState.isSubmitting
                ? t('security.email.submitting')
                : t('security.email.submit')}
            </Button>
            <Button
              type="button"
              variant="ghost"
              onClick={() => {
                setMode({ kind: 'idle' });
                setFormError(null);
                form.reset();
              }}
            >
              {t('security.email.cancel')}
            </Button>
          </div>
        </form>
      )}
    </section>
  );
}
