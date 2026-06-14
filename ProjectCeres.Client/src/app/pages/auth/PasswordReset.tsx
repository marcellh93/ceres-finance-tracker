import { useEffect, useMemo, useRef, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { InputOTP, InputOTPGroup, InputOTPSlot } from '@/components/ui/input-otp';
import { Field } from '../../components/Field';
import {
  passwordResetRequestSchema,
  type PasswordResetRequestFormValues,
  passwordResetConfirmSchema,
  type PasswordResetConfirmFormValues,
} from '../../auth/schemas/password-reset.schema';
import { apiFetch } from '../../lib/api-client';
import { getCachedXsrfRequestToken, setCachedXsrfRequestToken } from '../../auth/csrf';
import { readTokenFromHash } from '../../lib/url-hash-token';

type ConfirmOutcome =
  | { kind: 'ok' }
  | { kind: 'requiresTotp' }
  | { kind: 'invalidToken' }
  | { kind: 'invalidTotp' }
  | { kind: 'policyViolation'; fieldErrors: Record<string, string> }
  | { kind: 'network' };

async function ensureCsrf(): Promise<void> {
  if (getCachedXsrfRequestToken()) return;
  const response = await fetch('/api/auth/csrf', { method: 'GET', credentials: 'include' });
  const token = response.headers.get('X-XSRF-TOKEN');
  if (token) setCachedXsrfRequestToken(token);
}

async function submitConfirm(
  token: string,
  newPassword: string,
  totpCode: string | undefined,
): Promise<ConfirmOutcome> {
  try {
    await ensureCsrf();
    const headers: Record<string, string> = { 'Content-Type': 'application/json' };
    const xsrf = getCachedXsrfRequestToken();
    if (xsrf) headers['X-XSRF-TOKEN'] = xsrf;
    const response = await fetch('/api/auth/password-reset/confirm', {
      method: 'POST',
      credentials: 'include',
      headers,
      body: JSON.stringify({ token, newPassword, totpCode: totpCode || undefined }),
    });
    if (response.status === 204) return { kind: 'ok' };
    if (response.status === 200) {
      const payload = (await response.json().catch(() => null)) as { requiresTotp?: boolean } | null;
      if (payload?.requiresTotp) return { kind: 'requiresTotp' };
      return { kind: 'ok' };
    }
    if (response.status === 401) {
      const payload = (await response.json().catch(() => null)) as { error?: { code?: string } } | null;
      const code = payload?.error?.code;
      if (code === 'INVALID_MFA_CODE') return { kind: 'invalidTotp' };
      return { kind: 'invalidToken' };
    }
    if (response.status === 422) {
      const payload = (await response.json().catch(() => null)) as {
        error?: { details?: Array<{ field: string; message: string }> };
      } | null;
      const fieldErrors: Record<string, string> = {};
      for (const d of payload?.error?.details ?? []) {
        fieldErrors[d.field] = d.message;
      }
      return { kind: 'policyViolation', fieldErrors };
    }
    return { kind: 'invalidToken' };
  } catch {
    return { kind: 'network' };
  }
}

export function PasswordReset() {
  const location = useLocation();
  const token = useMemo(() => readTokenFromHash(location.hash), [location.hash]);
  return token ? <ConfirmForm token={token} /> : <RequestForm />;
}

function RequestForm() {
  const { t } = useTranslation();
  const [submitted, setSubmitted] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [retrySeconds, setRetrySeconds] = useState<number | null>(null);

  const form = useForm<PasswordResetRequestFormValues>({
    resolver: zodResolver(passwordResetRequestSchema),
    defaultValues: { email: '' },
  });

  useEffect(() => {
    if (retrySeconds == null || retrySeconds <= 0) return;
    const id = window.setTimeout(() => {
      const next = (retrySeconds ?? 0) - 1;
      if (next <= 0) {
        setRetrySeconds(null);
        setErrorMessage(null);
      } else {
        setRetrySeconds(next);
        setErrorMessage(t('auth.passwordReset.request.errors.tooManyAttempts', { seconds: next }));
      }
    }, 1000);
    return () => window.clearTimeout(id);
  }, [retrySeconds, t]);

  const onSubmit = async (values: PasswordResetRequestFormValues) => {
    setErrorMessage(null);
    setRetrySeconds(null);
    const result = await apiFetch('/api/auth/password-reset/request', {
      method: 'POST',
      body: { email: values.email },
    });
    if (result.ok) {
      setSubmitted(true);
      return;
    }
    if (result.status === 429) {
      // apiFetch doesn't surface Retry-After; we can't read the header here.
      // Fall back to the no-countdown variant. The user can retry; the server
      // will still gate them. Acceptable for this surface.
      setErrorMessage(t('auth.passwordReset.request.errors.tooManyAttemptsNoCountdown'));
      return;
    }
    setErrorMessage(t('auth.passwordReset.request.errors.network'));
  };

  if (submitted) {
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold tracking-tight">
          {t('auth.passwordReset.request.successTitle')}
        </h1>
        <p className="text-sm text-muted-foreground">
          {t('auth.passwordReset.request.successBody')}
        </p>
        <div className="text-sm">
          <Link to="/login" className="underline-offset-4 hover:underline">
            {t('auth.passwordReset.request.backToSignIn')}
          </Link>
        </div>
      </div>
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4" noValidate>
      <h1 className="text-xl font-semibold tracking-tight">{t('auth.passwordReset.request.title')}</h1>
      <p className="text-sm text-muted-foreground">{t('auth.passwordReset.request.description')}</p>

      <Field
        label={t('auth.passwordReset.request.emailLabel')}
        htmlFor="email"
        error={form.formState.errors.email?.message ? t(form.formState.errors.email.message) : undefined}
      >
        <Input
          id="email"
          type="email"
          autoComplete="email"
          autoFocus
          aria-describedby={form.formState.errors.email ? 'email-error' : undefined}
          {...form.register('email')}
        />
      </Field>

      {errorMessage && (
        <div role="alert" aria-live="polite" className="text-sm text-destructive">
          {errorMessage}
        </div>
      )}

      <Button type="submit" className="w-full" disabled={form.formState.isSubmitting}>
        {form.formState.isSubmitting
          ? t('auth.passwordReset.request.submitting')
          : t('auth.passwordReset.request.submit')}
      </Button>

      <div className="text-sm">
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t('auth.passwordReset.request.backToSignIn')}
        </Link>
      </div>
    </form>
  );
}

function ConfirmForm({ token }: { token: string }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [needsTotp, setNeedsTotp] = useState(false);
  const [tokenInvalid, setTokenInvalid] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const submitInFlightRef = useRef(false);

  const form = useForm<PasswordResetConfirmFormValues>({
    resolver: zodResolver(passwordResetConfirmSchema),
    defaultValues: { newPassword: '', confirmPassword: '', totpCode: '' },
  });

  const totpValue = form.watch('totpCode') ?? '';

  const onSubmit = async (values: PasswordResetConfirmFormValues) => {
    if (submitInFlightRef.current) return;
    submitInFlightRef.current = true;
    setErrorMessage(null);
    try {
      const outcome = await submitConfirm(token, values.newPassword, values.totpCode);
      if (outcome.kind === 'ok') {
        navigate('/login?reset=1');
        return;
      }
      if (outcome.kind === 'requiresTotp') {
        setNeedsTotp(true);
        return;
      }
      if (outcome.kind === 'invalidToken') {
        setTokenInvalid(true);
        return;
      }
      if (outcome.kind === 'invalidTotp') {
        setErrorMessage(t('auth.passwordReset.confirm.errors.invalidTotp'));
        form.setValue('totpCode', '');
        return;
      }
      if (outcome.kind === 'policyViolation') {
        const entries = Object.entries(outcome.fieldErrors);
        let firstField: keyof PasswordResetConfirmFormValues | null = null;
        for (const [field, message] of entries) {
          const formField = field as keyof PasswordResetConfirmFormValues;
          form.setError(formField, { type: 'server', message });
          if (firstField === null) firstField = formField;
        }
        if (firstField !== null) form.setFocus(firstField);
        return;
      }
      setErrorMessage(t('auth.passwordReset.confirm.errors.network'));
    } finally {
      submitInFlightRef.current = false;
    }
  };

  if (tokenInvalid) {
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold tracking-tight">
          {t('auth.passwordReset.confirm.title')}
        </h1>
        <div role="alert" className="text-sm text-destructive">
          {t('auth.passwordReset.confirm.errors.invalidToken')}
        </div>
        <div className="text-sm">
          <Link to="/password-reset" className="underline-offset-4 hover:underline">
            {t('auth.passwordReset.confirm.requestNewLink')}
          </Link>
        </div>
        <div className="text-sm">
          <Link to="/login" className="underline-offset-4 hover:underline">
            {t('auth.passwordReset.confirm.backToSignIn')}
          </Link>
        </div>
      </div>
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4" noValidate>
      <h1 className="text-xl font-semibold tracking-tight">{t('auth.passwordReset.confirm.title')}</h1>
      <p className="text-sm text-muted-foreground">{t('auth.passwordReset.confirm.description')}</p>

      <Field
        label={t('auth.passwordReset.confirm.newPasswordLabel')}
        htmlFor="new-password"
        error={form.formState.errors.newPassword?.message ? t(form.formState.errors.newPassword.message) : undefined}
      >
        <Input
          id="new-password"
          type="password"
          autoComplete="new-password"
          autoFocus
          aria-describedby={form.formState.errors.newPassword ? 'new-password-error' : undefined}
          {...form.register('newPassword')}
        />
      </Field>

      <Field
        label={t('auth.passwordReset.confirm.confirmPasswordLabel')}
        htmlFor="confirm-password"
        error={form.formState.errors.confirmPassword?.message ? t(form.formState.errors.confirmPassword.message) : undefined}
      >
        <Input
          id="confirm-password"
          type="password"
          autoComplete="new-password"
          aria-describedby={form.formState.errors.confirmPassword ? 'confirm-password-error' : undefined}
          {...form.register('confirmPassword')}
        />
      </Field>

      {needsTotp && (
        <div className="space-y-1.5">
          <div className="text-sm font-medium tracking-wide text-foreground/80 select-none">
            {t('auth.passwordReset.confirm.totpLabel')}
          </div>
          <p className="text-xs text-muted-foreground">{t('auth.passwordReset.confirm.totpHint')}</p>
          <InputOTP
            maxLength={6}
            value={totpValue}
            onChange={(v) => form.setValue('totpCode', v, { shouldValidate: false })}
            aria-label={t('auth.passwordReset.confirm.totpLabel')}
            autoFocus
          >
            <InputOTPGroup>
              <InputOTPSlot index={0} />
              <InputOTPSlot index={1} />
              <InputOTPSlot index={2} />
              <InputOTPSlot index={3} />
              <InputOTPSlot index={4} />
              <InputOTPSlot index={5} />
            </InputOTPGroup>
          </InputOTP>
        </div>
      )}

      {errorMessage && (
        <div role="alert" aria-live="assertive" className="text-sm text-destructive">
          {errorMessage}
        </div>
      )}

      <Button type="submit" className="w-full" disabled={form.formState.isSubmitting}>
        {form.formState.isSubmitting
          ? t('auth.passwordReset.confirm.submitting')
          : t('auth.passwordReset.confirm.submit')}
      </Button>

      <div className="text-sm">
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t('auth.passwordReset.confirm.backToSignIn')}
        </Link>
      </div>
    </form>
  );
}
