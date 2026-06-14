import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Field } from '../../components/Field';
import { apiFetch, NetworkError } from '../../lib/api-client';
import { useAuth } from '../../auth/auth-context';
import { setCachedXsrfRequestToken } from '../../auth/csrf';
import { loginSchema, type LoginFormValues } from '../../auth/schemas/login.schema';

type ServerErrorState =
  | { kind: 'none' }
  | { kind: 'emailNotConfirmed' }
  | { kind: 'accountLocked' }
  | { kind: 'server' };

export function Login() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const auth = useAuth();
  const [serverError, setServerError] = useState<ServerErrorState>({ kind: 'none' });
  const [resending, setResending] = useState(false);
  const [resendError, setResendError] = useState<string | null>(null);
  const [resendSucceeded, setResendSucceeded] = useState(false);

  useEffect(() => {
    // Sonner's id-based dedup: passing the same id twice collapses to one
    // toast. Without an id, React 19 StrictMode's dev-mode double-effect-invoke
    // produces two stacked toasts. Each query-param trigger gets a stable id
    // so the second invoke is a no-op visually.
    if (searchParams.get('expired') === '1') {
      toast(t('auth.login.toasts.totpExpired'), { id: 'login-totp-expired' });
    }
    if (searchParams.get('reset') === '1') {
      toast(t('auth.login.toasts.passwordResetSuccess'), { id: 'login-password-reset-success' });
    }
    if (searchParams.get('unlocked') === '1') {
      toast(t('auth.accountUnlock.toastSucceeded'), { id: 'login-account-unlocked' });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const form = useForm<LoginFormValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: '', password: '', rememberMe: false },
  });
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
    setError,
    setFocus,
  } = form;

  const onSubmit = async (values: LoginFormValues) => {
    setServerError({ kind: 'none' });
    try {
      const result = await apiFetch<{ requiresTotp?: boolean }>('/api/auth/login', {
        method: 'POST',
        body: values,
      });

      if (result.ok) {
        // Server rotates the CSRF cookie+request-token pair on successful
        // login (AuthController.IssueSessionAndCookiesAsync calls
        // _antiforgery.GetAndStoreTokens). Clear our cached request token
        // so the next state-changing call (e.g. POST /api/auth/mfa/enroll
        // from /app/security) re-handshakes against the new cookie. Without
        // this clear, the SPA sends the stale request-token against the
        // fresh cookie and the server rejects with 400 — same anti-pattern
        // 9.1.5.i fixed for the logout path. Applies to BOTH the no-MFA
        // success (here) and the requiresTotp branch (the TOTP-pending
        // cookie counts as a rotated CSRF state too).
        setCachedXsrfRequestToken(null);
        if (result.data?.requiresTotp) {
          navigate('/login/totp');
          return;
        }
        await auth.refresh();
        const raw = searchParams.get('redirect') ?? '/';
        // Defense-in-depth: only accept same-origin relative paths to prevent
        // open-redirect attacks via crafted /login?redirect=<external> URLs.
        // RequireAuth always writes encoded relative paths so this only changes
        // behaviour for hand-crafted links.
        const redirectTo = raw.startsWith('/') && !raw.startsWith('//') ? raw : '/';
        navigate(redirectTo);
        return;
      }

      // Failure path — map known codes to UX.
      if (result.code === 'ACCOUNT_LOCKED_OUT') {
        setServerError({ kind: 'accountLocked' });
        return;
      }
      if (result.code === 'EMAIL_NOT_CONFIRMED') {
        setServerError({ kind: 'emailNotConfirmed' });
        return;
      }
      if (result.code === 'INVALID_CREDENTIALS') {
        setError('password', { type: 'server', message: t('auth.login.errors.invalidCredentials') });
        setFocus('password');
        return;
      }
      // 5xx — a server fault, not bad credentials. api-client maps any 5xx
      // with no envelope to code 'HTTP_500'; the status guard covers the rest.
      if (result.code === 'HTTP_500' || result.status >= 500) {
        setServerError({ kind: 'server' });
        return;
      }
      // 422 with field-level errors — map each field.
      if ('fieldErrors' in result) {
        const fields = Object.entries(result.fieldErrors);
        for (const [field, message] of fields) {
          setError(field as keyof LoginFormValues, { type: 'server', message });
        }
        if (fields.length > 0) setFocus(fields[0][0] as keyof LoginFormValues);
        return;
      }
      // Unknown failure — surface as a generic field error on password (the
      // safer of the two — never leak email-specific information).
      setError('password', { type: 'server', message: result.message });
      setFocus('password');
    } catch (err) {
      // Lost-connection: distinct from bad credentials. Toast it, don't
      // mislabel as a wrong password.
      if (err instanceof NetworkError) {
        toast(t('auth.login.errors.network'));
        return;
      }
      // Any other unexpected throw is a fault on our side, not the user's.
      setServerError({ kind: 'server' });
    }
  };

  const onResendVerification = async () => {
    setResending(true);
    setResendError(null);
    setResendSucceeded(false);
    try {
      const email = form.getValues('email');
      const result = await apiFetch('/api/auth/email/verify/resend', {
        method: 'POST',
        body: { email },
      });
      if (result.ok) {
        // Server returns 204 regardless of whether the email exists or is
        // already confirmed (anti-enumeration). UX matches the EmailVerify
        // page's resend acknowledgement copy.
        setResendSucceeded(true);
        return;
      }
      if (result.status === 429) {
        setResendError(t('auth.login.errors.resendTooMany'));
        return;
      }
      setResendError(t('auth.login.errors.resendFailed'));
    } catch {
      setResendError(t('auth.login.errors.resendFailed'));
    } finally {
      setResending(false);
    }
  };

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
      <h1 className="text-xl font-semibold tracking-tight">{t('auth.login.title')}</h1>

      <Field label={t('auth.login.emailLabel')} htmlFor="email" error={errors.email?.message ? t(errors.email.message) : undefined}>
        <Input
          id="email"
          type="email"
          autoComplete="email"
          autoFocus
          aria-describedby={errors.email ? 'email-error' : undefined}
          {...register('email')}
        />
      </Field>

      <Field label={t('auth.login.passwordLabel')} htmlFor="password" error={errors.password?.message ? t(errors.password.message) : undefined}>
        <Input
          id="password"
          type="password"
          autoComplete="current-password"
          aria-describedby={errors.password ? 'password-error' : undefined}
          {...register('password')}
        />
      </Field>

      {serverError.kind === 'accountLocked' && (
        <p className="text-sm text-destructive" role="alert">
          {t('auth.login.errors.accountLocked')}
        </p>
      )}

      {serverError.kind === 'server' && (
        <p className="text-sm text-destructive" role="alert">
          {t('auth.login.errors.server')}
        </p>
      )}

      {serverError.kind === 'emailNotConfirmed' && (
        <div className="text-sm" role="status">
          {resendSucceeded ? (
            <p className="text-muted-foreground" aria-live="polite">
              {t('auth.login.resendSuccess')}
            </p>
          ) : (
            <Button
              type="button"
              variant="link"
              onClick={onResendVerification}
              disabled={resending}
              className="px-0"
            >
              {t('auth.login.resendVerification')}
            </Button>
          )}
          {resendError && (
            <p className="text-xs text-destructive" role="alert">{resendError}</p>
          )}
        </div>
      )}

      <Button type="submit" className="w-full" disabled={isSubmitting}>
        {isSubmitting ? t('auth.login.submitting') : t('auth.login.submit')}
      </Button>

      <div className="flex items-center justify-between text-sm">
        <Link to="/password-reset" className="underline-offset-4 hover:underline">
          {t('auth.login.forgotPasswordLink')}
        </Link>
        <Link to="/register" className="underline-offset-4 hover:underline">
          {t('auth.login.createAccountLink')}
        </Link>
      </div>

      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" {...register('rememberMe')} />
        {t('auth.login.rememberMeLabel')}
      </label>
    </form>
  );
}
