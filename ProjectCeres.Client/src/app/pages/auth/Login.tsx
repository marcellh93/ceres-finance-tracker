import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Field } from '../../components/Field';
import { apiFetch } from '../../lib/api-client';
import { useAuth } from '../../auth/auth-context';
import { loginSchema, type LoginFormValues } from '../../auth/schemas/login.schema';

type ServerErrorState =
  | { kind: 'none' }
  | { kind: 'emailNotConfirmed' };

export function Login() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const auth = useAuth();
  const [serverError, setServerError] = useState<ServerErrorState>({ kind: 'none' });
  const [resending, setResending] = useState(false);
  const [resendError, setResendError] = useState<string | null>(null);

  useEffect(() => {
    if (searchParams.get('expired') === '1') {
      toast(t('auth.login.toasts.totpExpired'));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
    setError,
  } = useForm<LoginFormValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: '', password: '', rememberMe: false },
  });

  const onSubmit = async (values: LoginFormValues) => {
    setServerError({ kind: 'none' });
    try {
      const result = await apiFetch<{ requiresTotp?: boolean }>('/api/auth/login', {
        method: 'POST',
        body: values,
      });

      if (result.ok) {
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
        navigate('/account/unlock');
        return;
      }
      if (result.code === 'EMAIL_NOT_CONFIRMED') {
        setServerError({ kind: 'emailNotConfirmed' });
        return;
      }
      if (result.code === 'INVALID_CREDENTIALS') {
        setError('password', { type: 'server', message: t('auth.login.errors.invalidCredentials') });
        return;
      }
      // 422 with field-level errors — map each field.
      if ('fieldErrors' in result) {
        for (const [field, message] of Object.entries(result.fieldErrors)) {
          setError(field as keyof LoginFormValues, { type: 'server', message });
        }
        return;
      }
      // Unknown failure — surface as a generic field error on password (the
      // safer of the two — never leak email-specific information).
      setError('password', { type: 'server', message: result.message });
    } catch {
      // Network / 5xx — toast handling lives in a shared error boundary in
      // a later commit; for now, surface a generic password field error.
      setError('password', { type: 'server', message: t('auth.login.errors.invalidCredentials') });
    }
  };

  const onResendVerification = async () => {
    setResending(true);
    setResendError(null);
    try {
      // Resend endpoint ships in Phase 2 (commit 9); this is a placeholder
      // call that will succeed once that endpoint exists. The button itself
      // should ship now so the EMAIL_NOT_CONFIRMED UX is complete the
      // moment Phase 2 lands.
      const result = await apiFetch('/api/auth/email/verify/resend', { method: 'POST', body: {} });
      if (!result.ok) {
        // Phase-1 placeholder: the endpoint doesn't exist until Phase 2, so result.ok is always false.
        // Generic message keeps the user informed without revealing endpoint shape.
        setResendError(t('auth.login.errors.resendFailed'));
      }
    } catch {
      setResendError(t('auth.login.errors.resendFailed'));
    } finally {
      setResending(false);
    }
  };

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
      <h1 className="text-xl font-semibold tracking-tight">{t('auth.login.title')}</h1>

      <Field label={t('auth.login.emailLabel')} htmlFor="email" error={errors.email?.message}>
        <Input
          id="email"
          type="email"
          autoComplete="email"
          autoFocus
          {...register('email')}
        />
      </Field>

      <Field label={t('auth.login.passwordLabel')} htmlFor="password" error={errors.password?.message}>
        <Input
          id="password"
          type="password"
          autoComplete="current-password"
          {...register('password')}
        />
      </Field>

      {serverError.kind === 'emailNotConfirmed' && (
        <div className="text-sm" role="status">
          <Button
            type="button"
            variant="link"
            onClick={onResendVerification}
            disabled={resending}
            className="px-0"
          >
            {t('auth.login.resendVerification')}
          </Button>
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
