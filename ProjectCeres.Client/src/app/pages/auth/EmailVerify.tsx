import { useEffect, useMemo, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Link, useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Field } from '../../components/Field';
import { readTokenFromHash } from '../../lib/url-hash-token';
import { apiFetch } from '../../lib/api-client';

// Resend form schema — i18n key for the error so it renders in the active
// language. See login.schema.ts / register.schema.ts for the rationale.
const resendSchema = z.object({
  email: z.string().email('auth.register.errors.invalidEmail'),
});
type ResendFormValues = z.infer<typeof resendSchema>;

type VerifyState = 'verifying' | 'success' | 'invalid';

export function EmailVerify() {
  const { t } = useTranslation();
  const location = useLocation();
  const token = useMemo(() => readTokenFromHash(location.hash), [location.hash]);
  const [state, setState] = useState<VerifyState>(token ? 'verifying' : 'invalid');

  useEffect(() => {
    if (state !== 'verifying' || !token) return;
    let cancelled = false;
    (async () => {
      try {
        const result = await apiFetch('/api/auth/email/verify', {
          method: 'POST',
          body: { token },
        });
        if (cancelled) return;
        setState(result.ok ? 'success' : 'invalid');
      } catch {
        if (cancelled) return;
        setState('invalid');
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [state, token]);

  if (state === 'verifying') {
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold tracking-tight">{t('auth.emailVerify.title')}</h1>
        <p className="text-sm text-muted-foreground" role="status" aria-live="polite">
          {t('auth.emailVerify.verifyingMessage')}
        </p>
      </div>
    );
  }

  if (state === 'success') {
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold tracking-tight">
          {t('auth.emailVerify.successTitle')}
        </h1>
        <p className="text-sm text-muted-foreground">{t('auth.emailVerify.successBody')}</p>
        <div className="text-sm">
          <Link to="/login" className="underline-offset-4 hover:underline">
            {t('auth.emailVerify.signInLink')}
          </Link>
        </div>
      </div>
    );
  }

  return <InvalidBlock />;
}

function InvalidBlock() {
  const { t } = useTranslation();
  const [resendMode, setResendMode] = useState<'idle' | 'form' | 'sent'>('idle');
  const [resendError, setResendError] = useState<string | null>(null);

  const form = useForm<ResendFormValues>({
    resolver: zodResolver(resendSchema),
    defaultValues: { email: '' },
  });

  const onResend = async (values: ResendFormValues) => {
    setResendError(null);
    try {
      const result = await apiFetch('/api/auth/email/verify/resend', {
        method: 'POST',
        body: { email: values.email },
      });
      // Anti-enum: server returns 204 regardless of whether the email
      // exists or is already confirmed. UX is the same on either branch.
      if (result.ok) {
        setResendMode('sent');
        return;
      }
      setResendError(t('auth.emailVerify.errors.resendFailed'));
    } catch {
      setResendError(t('auth.emailVerify.errors.network'));
    }
  };

  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold tracking-tight">
        {t('auth.emailVerify.invalidTitle')}
      </h1>
      <p className="text-sm text-muted-foreground" role="alert">
        {t('auth.emailVerify.invalidBody')}
      </p>

      {resendMode === 'idle' && (
        <Button type="button" onClick={() => setResendMode('form')} className="w-full">
          {t('auth.emailVerify.resendButton')}
        </Button>
      )}

      {resendMode === 'form' && (
        <form onSubmit={form.handleSubmit(onResend)} className="space-y-3" noValidate>
          <Field
            label={t('auth.emailVerify.resendPromptLabel')}
            htmlFor="resend-email"
            error={
              form.formState.errors.email?.message
                ? t(form.formState.errors.email.message)
                : undefined
            }
          >
            <Input
              id="resend-email"
              type="email"
              autoComplete="email"
              autoFocus
              {...form.register('email')}
            />
          </Field>

          {resendError && (
            <div role="alert" aria-live="polite" className="text-sm text-destructive">
              {resendError}
            </div>
          )}

          <Button type="submit" className="w-full" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting
              ? t('auth.emailVerify.resendSubmitting')
              : t('auth.emailVerify.resendSubmit')}
          </Button>
        </form>
      )}

      {resendMode === 'sent' && (
        <p className="text-sm text-muted-foreground" role="status" aria-live="polite">
          {t('auth.emailVerify.resendSuccess')}
        </p>
      )}

      <div className="text-sm">
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t('auth.emailVerify.signInLink')}
        </Link>
      </div>
    </div>
  );
}
