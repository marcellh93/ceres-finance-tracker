import { useEffect, useRef, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Link, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { InputOTP, InputOTPGroup, InputOTPSlot } from '@/components/ui/input-otp';
import { Field } from '../../components/Field';
import { useAuth } from '../../auth/auth-context';
import {
  totpCodeSchema,
  type TotpCodeFormValues,
  backupCodeSchema,
  type BackupCodeFormValues,
} from '../../auth/schemas/login-totp.schema';
import { getCachedXsrfRequestToken, setCachedXsrfRequestToken } from '../../auth/csrf';

type SubmitOutcome =
  | { kind: 'ok' }
  | { kind: 'invalid' }
  | { kind: 'lockedOut' }
  | { kind: 'unauthenticated' }
  | { kind: 'rateLimited'; retryAfterSeconds: number | null }
  | { kind: 'network' };

async function ensureCsrf(): Promise<void> {
  if (getCachedXsrfRequestToken()) return;
  const response = await fetch('/api/auth/csrf', { method: 'GET', credentials: 'include' });
  const token = response.headers.get('X-XSRF-TOKEN');
  if (token) setCachedXsrfRequestToken(token);
}

async function submitTotp(code: string): Promise<SubmitOutcome> {
  try {
    await ensureCsrf();
    const headers: Record<string, string> = { 'Content-Type': 'application/json' };
    const token = getCachedXsrfRequestToken();
    if (token) headers['X-XSRF-TOKEN'] = token;
    const response = await fetch('/api/auth/login/totp', {
      method: 'POST',
      credentials: 'include',
      headers,
      body: JSON.stringify({ code }),
    });
    if (response.status === 204) return { kind: 'ok' };
    if (response.status === 429) {
      const raw = response.headers.get('Retry-After');
      const parsed = raw ? Number.parseInt(raw, 10) : Number.NaN;
      return { kind: 'rateLimited', retryAfterSeconds: Number.isFinite(parsed) ? parsed : null };
    }
    if (response.status === 401) {
      const payload = (await response.json().catch(() => null)) as { error?: { code?: string } } | null;
      const code = payload?.error?.code;
      if (code === 'ACCOUNT_LOCKED_OUT') return { kind: 'lockedOut' };
      if (code === 'UNAUTHENTICATED') return { kind: 'unauthenticated' };
      return { kind: 'invalid' };
    }
    return { kind: 'invalid' };
  } catch {
    return { kind: 'network' };
  }
}

export function LoginTotp() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const auth = useAuth();
  const [mode, setMode] = useState<'totp' | 'backup'>('totp');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [retrySeconds, setRetrySeconds] = useState<number | null>(null);
  const submitInFlightRef = useRef(false);

  const totpForm = useForm<TotpCodeFormValues>({
    resolver: zodResolver(totpCodeSchema),
    defaultValues: { code: '' },
    mode: 'onSubmit',
  });

  const backupForm = useForm<BackupCodeFormValues>({
    resolver: zodResolver(backupCodeSchema),
    defaultValues: { code: '' },
    mode: 'onSubmit',
  });

  const handleOutcome = async (outcome: SubmitOutcome, onInvalidReset: () => void) => {
    if (outcome.kind === 'ok') {
      // Server rotates the CSRF pair on TOTP success (same as plain login —
      // AuthController.IssueSessionAndCookiesAsync calls _antiforgery.
      // GetAndStoreTokens). Clear cache so the next state-changing call
      // re-handshakes. See the matching comment in Login.tsx for the long
      // explanation. Without this, /app/security's first enroll POST 400s.
      setCachedXsrfRequestToken(null);
      await auth.refresh();
      navigate('/');
      return;
    }
    if (outcome.kind === 'lockedOut') {
      navigate('/account/unlock');
      return;
    }
    if (outcome.kind === 'unauthenticated') {
      navigate('/login?expired=1');
      return;
    }
    if (outcome.kind === 'rateLimited') {
      if (outcome.retryAfterSeconds != null) {
        setRetrySeconds(outcome.retryAfterSeconds);
        setErrorMessage(t('auth.totp.errors.tooManyAttempts', { seconds: outcome.retryAfterSeconds }));
      } else {
        setRetrySeconds(null);
        setErrorMessage(t('auth.totp.errors.tooManyAttemptsNoCountdown'));
      }
      return;
    }
    if (outcome.kind === 'network') {
      setErrorMessage(t('auth.totp.errors.network'));
      return;
    }
    setErrorMessage(t('auth.totp.errors.invalid'));
    onInvalidReset();
  };

  useEffect(() => {
    if (retrySeconds == null || retrySeconds <= 0) return;
    const id = window.setTimeout(() => {
      const next = (retrySeconds ?? 0) - 1;
      if (next <= 0) {
        setRetrySeconds(null);
        setErrorMessage(null);
      } else {
        setRetrySeconds(next);
        setErrorMessage(t('auth.totp.errors.tooManyAttempts', { seconds: next }));
      }
    }, 1000);
    return () => window.clearTimeout(id);
  }, [retrySeconds, t]);

  const onTotpSubmit = async (values: TotpCodeFormValues) => {
    if (submitInFlightRef.current) return;
    submitInFlightRef.current = true;
    setErrorMessage(null);
    try {
      const outcome = await submitTotp(values.code);
      await handleOutcome(outcome, () => {
        totpForm.reset({ code: '' });
      });
    } finally {
      submitInFlightRef.current = false;
    }
  };

  const onBackupSubmit = async (values: BackupCodeFormValues) => {
    if (submitInFlightRef.current) return;
    submitInFlightRef.current = true;
    setErrorMessage(null);
    try {
      const outcome = await submitTotp(values.code);
      await handleOutcome(outcome, () => {
        backupForm.reset({ code: '' });
      });
    } finally {
      submitInFlightRef.current = false;
    }
  };

  const switchToBackup = () => {
    setMode('backup');
    setErrorMessage(null);
    setRetrySeconds(null);
    totpForm.reset({ code: '' });
  };

  const switchToTotp = () => {
    setMode('totp');
    setErrorMessage(null);
    setRetrySeconds(null);
    backupForm.reset({ code: '' });
  };

  // eslint-disable-next-line react-hooks/incompatible-library -- Why: React Hook Form's watch() is used as a plain derived value for the auto-submit effect, not passed to a memoized child; the concurrent-rendering risk does not apply in this linear auth flow.
  const codeValue = totpForm.watch('code');
  useEffect(() => {
    if (mode !== 'totp') return;
    if (codeValue.length === 6 && /^\d{6}$/.test(codeValue) && !submitInFlightRef.current) {
      void totpForm.handleSubmit(onTotpSubmit)();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- Why: onTotpSubmit and totpForm are intentionally excluded; they change on every render but are stable in practice, and including them would cause the auto-submit to re-trigger spuriously.
  }, [codeValue, mode]);

  const isSubmitting = totpForm.formState.isSubmitting || backupForm.formState.isSubmitting;
  const rateLimited = retrySeconds != null;

  if (mode === 'totp') {
    return (
      <form onSubmit={totpForm.handleSubmit(onTotpSubmit)} className="space-y-4" noValidate>
        <h1 className="text-xl font-semibold tracking-tight">{t('auth.totp.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('auth.totp.description')}</p>

        <div className="space-y-2">
          <InputOTP
            maxLength={6}
            value={codeValue}
            onChange={(v) => totpForm.setValue('code', v, { shouldValidate: false })}
            aria-label={t('auth.totp.codeLabel')}
            autoFocus
            disabled={rateLimited || isSubmitting}
          >
            <InputOTPGroup aria-invalid={errorMessage != null ? true : undefined}>
              <InputOTPSlot index={0} />
              <InputOTPSlot index={1} />
              <InputOTPSlot index={2} />
              <InputOTPSlot index={3} />
              <InputOTPSlot index={4} />
              <InputOTPSlot index={5} />
            </InputOTPGroup>
          </InputOTP>

          {errorMessage && (
            <div
              role="alert"
              aria-live={rateLimited ? 'polite' : 'assertive'}
              className="text-sm text-destructive"
            >
              {errorMessage}
            </div>
          )}
        </div>

        <Button
          type="submit"
          className="w-full"
          disabled={codeValue.length !== 6 || isSubmitting || rateLimited}
        >
          {isSubmitting ? t('auth.totp.submitting') : t('auth.totp.submit')}
        </Button>

        <div className="text-sm">
          <Button type="button" variant="link" onClick={switchToBackup} className="px-0">
            {t('auth.totp.backupCodePrompt')}
          </Button>
        </div>

        <div className="text-sm">
          <Link to="/login" className="underline-offset-4 hover:underline">
            {t('auth.totp.backToSignIn')}
          </Link>
        </div>
      </form>
    );
  }

  return (
    <form onSubmit={backupForm.handleSubmit(onBackupSubmit)} className="space-y-4" noValidate>
      <h1 className="text-xl font-semibold tracking-tight">{t('auth.totp.title')}</h1>
      <p className="text-sm text-muted-foreground">{t('auth.totp.backupCodeDescription')}</p>

      <Field
        label={t('auth.totp.backupCodeLabel')}
        htmlFor="backup-code"
        error={backupForm.formState.errors.code?.message ? t(backupForm.formState.errors.code.message) : undefined}
      >
        <Input
          id="backup-code"
          type="text"
          autoComplete="one-time-code"
          autoFocus
          aria-describedby={backupForm.formState.errors.code ? 'backup-code-error' : undefined}
          {...backupForm.register('code')}
          disabled={rateLimited || isSubmitting}
        />
      </Field>

      {errorMessage && (
        <div
          role="alert"
          aria-live={rateLimited ? 'polite' : 'assertive'}
          className="text-sm text-destructive"
        >
          {errorMessage}
        </div>
      )}

      <Button type="submit" className="w-full" disabled={isSubmitting || rateLimited}>
        {isSubmitting ? t('auth.totp.submitting') : t('auth.totp.submit')}
      </Button>

      <div className="text-sm">
        <Button type="button" variant="link" onClick={switchToTotp} className="px-0">
          {t('auth.totp.useTotpInstead')}
        </Button>
      </div>

      <div className="text-sm">
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t('auth.totp.backToSignIn')}
        </Link>
      </div>
    </form>
  );
}
