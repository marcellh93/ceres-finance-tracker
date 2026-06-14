import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Field } from '../../components/Field';
import { registerSchema, type RegisterFormValues } from '../../auth/schemas/register.schema';
import { apiFetch, NetworkError } from '../../lib/api-client';

export function Register() {
  const { t } = useTranslation();
  const [submitted, setSubmitted] = useState<{ email: string } | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const form = useForm<RegisterFormValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: { email: '', password: '' },
  });

  const onSubmit = async (values: RegisterFormValues) => {
    setErrorMessage(null);
    try {
      const result = await apiFetch('/api/auth/register', {
        method: 'POST',
        body: { email: values.email, password: values.password },
      });
      if (result.ok) {
        setSubmitted({ email: values.email });
        return;
      }
      if (result.status === 422 && 'fieldErrors' in result) {
        for (const [field, message] of Object.entries(result.fieldErrors)) {
          // Server uses PascalCase field names ("Password"); map to camelCase
          // form field. The message is an i18n key (e.g.
          // "auth.register.errors.passwordBreached"), resolved via t() at
          // render time the same way zod schema keys are.
          const formField =
            (field.charAt(0).toLowerCase() + field.slice(1)) as keyof RegisterFormValues;
          form.setError(formField, { type: 'server', message });
        }
        return;
      }
      setErrorMessage(t('auth.register.errors.network'));
    } catch (err) {
      if (err instanceof NetworkError) {
        setErrorMessage(t('auth.register.errors.network'));
        return;
      }
      setErrorMessage(t('auth.register.errors.network'));
    }
  };

  if (submitted) {
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold tracking-tight">
          {t('auth.register.successTitle')}
        </h1>
        <p className="text-sm text-muted-foreground">
          {t('auth.register.successBody', { email: submitted.email })}
        </p>
        <div className="text-sm">
          <Link to="/login" className="underline-offset-4 hover:underline">
            {t('auth.register.backToSignIn')}
          </Link>
        </div>
      </div>
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4" noValidate>
      <h1 className="text-xl font-semibold tracking-tight">{t('auth.register.title')}</h1>
      <p className="text-sm text-muted-foreground">{t('auth.register.description')}</p>

      <Field
        label={t('auth.register.emailLabel')}
        htmlFor="email"
        error={
          form.formState.errors.email?.message
            ? t(form.formState.errors.email.message)
            : undefined
        }
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

      <Field
        label={t('auth.register.passwordLabel')}
        htmlFor="password"
        error={
          form.formState.errors.password?.message
            ? t(form.formState.errors.password.message)
            : undefined
        }
      >
        <Input
          id="password"
          type="password"
          autoComplete="new-password"
          aria-describedby={form.formState.errors.password ? 'password-hint password-error' : 'password-hint'}
          {...form.register('password')}
        />
        <p id="password-hint" className="text-xs text-muted-foreground">
          {t('auth.register.passwordHint')}
        </p>
      </Field>

      {errorMessage && (
        <div role="alert" aria-live="polite" className="text-sm text-destructive">
          {errorMessage}
        </div>
      )}

      <Button type="submit" className="w-full" disabled={form.formState.isSubmitting}>
        {form.formState.isSubmitting
          ? t('auth.register.submitting')
          : t('auth.register.submit')}
      </Button>

      <div className="text-sm">
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t('auth.register.backToSignIn')}
        </Link>
      </div>
    </form>
  );
}
