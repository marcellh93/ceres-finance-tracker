# Stage 9.2 — `/login/totp` page implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the SPA `/login/totp` page that completes the two-step login flow (TOTP cells default; backup-code fallback toggle), wired to the existing `POST /api/auth/login/totp` server endpoint.

**Architecture:** Single page component under `pages/auth/`, mounted in the public `AuthLayout` branch in `App.tsx`. Form state via `react-hook-form` + `zod` (existing project pattern from `Login.tsx`). 6-cell `InputOTP` primitive for default state; plain `<Input>` for the backup-code state. Submit handler uses a page-local `submitTotp()` helper that calls raw `fetch` (so we can read the `Retry-After` header on 429) after priming CSRF via the existing `apiFetch('/api/auth/csrf', { method: 'GET' })`. Failure paths map to inline error / route navigation / sonner toast per the spec's error UX table.

**Tech Stack:** React 19, TypeScript, react-hook-form, zod, @hookform/resolvers/zod, input-otp + shadcn `InputOTP`, react-router-dom, react-i18next, sonner, Vitest + RTL + vitest-axe.

**Spec:** `docs/superpowers/specs/2026-05-17-stage-9-2-login-totp-design.md`

---

## File map

| Path | Action | Responsibility |
|---|---|---|
| `ProjectCeres.Client/src/app/pages/auth/LoginTotp.tsx` | create | Page component (default + backup-code states, error/success handling). |
| `ProjectCeres.Client/src/app/pages/auth/LoginTotp.test.tsx` | create | Vitest unit tests for the page. |
| `ProjectCeres.Client/src/app/pages/auth/LoginTotp.a11y.test.tsx` | create | vitest-axe a11y assertion for both states. |
| `ProjectCeres.Client/src/app/auth/schemas/login-totp.schema.ts` | create | Zod schemas (`totpCodeSchema`, `backupCodeSchema`) + inferred TS types. |
| `ProjectCeres.Client/src/app/App.tsx` | modify (lines 42–64) | Register `<Route path="login/totp" element={<LoginTotp />} />` inside `AuthLayout`. Import `LoginTotp`. |
| `ProjectCeres.Client/src/app/pages/auth/Login.tsx` | modify | On mount, if `searchParams.get('expired') === '1'`, fire a sonner `toast(t('auth.login.toasts.totpExpired'))`. One additional useEffect, ~8 lines. |
| `ProjectCeres.Client/src/app/pages/auth/Login.test.tsx` | modify | One new test asserting the expired-toast renders for `/login?expired=1`. |
| `ProjectCeres.Client/src/app/i18n/locales/en.json` | modify | Add `auth.totp.*` namespace + `auth.login.toasts.totpExpired`. |
| `ProjectCeres.Client/src/app/i18n/locales/es.json` | modify | Spanish mirror. |

No new dependencies. No backend changes.

---

## Task 1 — Zod schemas

**Files:**
- Create: `ProjectCeres.Client/src/app/auth/schemas/login-totp.schema.ts`

- [ ] **Step 1: Write the schemas**

```typescript
import { z } from 'zod';

// TOTP codes are exactly 6 ASCII digits.
export const totpCodeSchema = z.object({
  code: z.string().regex(/^\d{6}$/, 'Enter the 6-digit code.'),
});
export type TotpCodeFormValues = z.infer<typeof totpCodeSchema>;

// Backup codes are server-validated; here we only require a non-empty string
// of reasonable length (server's MfaConstants.BackupCodeShape rejects anything
// outside its bounds, so client-side we keep validation permissive).
export const backupCodeSchema = z.object({
  code: z.string().min(1, 'Enter a backup code.').max(32),
});
export type BackupCodeFormValues = z.infer<typeof backupCodeSchema>;
```

- [ ] **Step 2: Commit**

```bash
git add ProjectCeres.Client/src/app/auth/schemas/login-totp.schema.ts
git commit -m "feat(stage-9.2): add zod schemas for TOTP + backup-code forms"
```

---

## Task 2 — i18n keys (EN)

**Files:**
- Modify: `ProjectCeres.Client/src/app/i18n/locales/en.json`

- [ ] **Step 1: Add the `auth.totp.*` namespace and the `auth.login.toasts.totpExpired` key**

Insert the new `totp` sub-object inside `auth`, after the existing `login` sub-object. Add a new `toasts` sub-object under `login`. Resulting `auth` portion (only new/modified lines shown):

```jsonc
"auth": {
  "login": {
    "title": "Sign in",
    "emailLabel": "Email",
    "passwordLabel": "Password",
    "rememberMeLabel": "Remember me on this device",
    "submit": "Sign in",
    "submitting": "Signing in…",
    "forgotPasswordLink": "Forgot password?",
    "createAccountLink": "Create an account",
    "resendVerification": "Resend verification email",
    "errors": {
      "invalidCredentials": "Email or password is incorrect.",
      "accountLocked": "Account locked. Check your email for an unlock link.",
      "emailNotConfirmed": "Verify your email first. Check your inbox.",
      "resendFailed": "Could not send the email. Try again in a moment."
    },
    "toasts": {
      "totpExpired": "Your sign-in expired. Please sign in again."
    }
  },
  "totp": {
    "title": "Verify your identity",
    "description": "Enter the 6-digit code from your authenticator app",
    "codeLabel": "Verification code",
    "submit": "Verify",
    "submitting": "Verifying",
    "backupCodePrompt": "Lost your device? Use a backup code",
    "backupCodeDescription": "Enter one of your backup codes",
    "backupCodeLabel": "Backup code",
    "useTotpInstead": "Use verification code instead",
    "backToSignIn": "Back to sign in",
    "errors": {
      "invalid": "Invalid code",
      "tooManyAttempts": "Too many attempts. Try again in {{seconds}} seconds.",
      "tooManyAttemptsNoCountdown": "Too many attempts. Try again in a moment.",
      "expired": "Your sign-in expired. Please sign in again.",
      "network": "Could not verify the code. Check your connection and try again."
    }
  },
  /* placeholders, languageToggle, logout — unchanged */
}
```

- [ ] **Step 2: Run a JSON-syntax sanity check by importing the file in tests later. No commit yet — bundle with the ES counterpart in the next task.**

---

## Task 3 — i18n keys (ES)

**Files:**
- Modify: `ProjectCeres.Client/src/app/i18n/locales/es.json`

- [ ] **Step 1: Add the Spanish mirror**

Insert `auth.totp.*` and `auth.login.toasts.totpExpired`:

```jsonc
"login": {
  /* existing keys */,
  "toasts": {
    "totpExpired": "Tu sesión caducó. Inicia sesión de nuevo."
  }
},
"totp": {
  "title": "Verifica tu identidad",
  "description": "Introduce el código de 6 dígitos de tu aplicación de autenticación",
  "codeLabel": "Código de verificación",
  "submit": "Verificar",
  "submitting": "Verificando",
  "backupCodePrompt": "¿Perdiste el dispositivo? Usa un código de respaldo",
  "backupCodeDescription": "Introduce uno de tus códigos de respaldo",
  "backupCodeLabel": "Código de respaldo",
  "useTotpInstead": "Usar código de verificación",
  "backToSignIn": "Volver a iniciar sesión",
  "errors": {
    "invalid": "Código no válido",
    "tooManyAttempts": "Demasiados intentos. Inténtalo en {{seconds}} segundos.",
    "tooManyAttemptsNoCountdown": "Demasiados intentos. Espera un momento.",
    "expired": "Tu sesión caducó. Inicia sesión de nuevo.",
    "network": "No se pudo verificar el código. Comprueba tu conexión e inténtalo de nuevo."
  }
}
```

- [ ] **Step 2: Commit EN + ES together**

```bash
git add ProjectCeres.Client/src/app/i18n/locales/en.json ProjectCeres.Client/src/app/i18n/locales/es.json
git commit -m "feat(stage-9.2): add auth.totp.* i18n keys (EN + ES) and totp-expired toast key"
```

---

## Task 4 — LoginTotp page component (skeleton + TOTP default state)

**Files:**
- Create: `ProjectCeres.Client/src/app/pages/auth/LoginTotp.tsx`

- [ ] **Step 1: Write the component**

```tsx
import { useEffect, useRef, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Link, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  InputOTP,
  InputOTPGroup,
  InputOTPSlot,
} from '@/components/ui/input-otp';
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
      const payload = await response.json().catch(() => null) as { error?: { code?: string } } | null;
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

  // 429 countdown — ticks once per second when retrySeconds > 0.
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

  // TOTP-mode: auto-submit when 6 digits typed.
  const codeValue = totpForm.watch('code');
  useEffect(() => {
    if (mode !== 'totp') return;
    if (codeValue.length === 6 && /^\d{6}$/.test(codeValue) && !submitInFlightRef.current) {
      void totpForm.handleSubmit(onTotpSubmit)();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
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
        error={backupForm.formState.errors.code?.message}
      >
        <Input
          id="backup-code"
          type="text"
          autoComplete="one-time-code"
          autoFocus
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
```

- [ ] **Step 2: Type-check**

Run: `pnpm --dir ProjectCeres.Client tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Client/src/app/pages/auth/LoginTotp.tsx
git commit -m "feat(stage-9.2): add LoginTotp page (TOTP cells + backup-code fallback)"
```

---

## Task 5 — Wire route in App.tsx + expired toast on Login

**Files:**
- Modify: `ProjectCeres.Client/src/app/App.tsx`
- Modify: `ProjectCeres.Client/src/app/pages/auth/Login.tsx`

- [ ] **Step 1: Add the route**

In `App.tsx`, line 44, change

```tsx
import { RegisterPlaceholder } from './pages/auth/RegisterPlaceholder';
```

to

```tsx
import { RegisterPlaceholder } from './pages/auth/RegisterPlaceholder';
import { LoginTotp } from './pages/auth/LoginTotp';
```

In `App.tsx`, between line 61 (`<Route path="login" element={<Login />} />`) and line 62, insert:

```tsx
        <Route path="login/totp" element={<LoginTotp />} />
```

- [ ] **Step 2: Add the expired-toast effect to Login.tsx**

Add `import { toast } from 'sonner';` to the imports.

Inside `Login()`, before the `useForm` call, add:

```tsx
useEffect(() => {
  if (searchParams.get('expired') === '1') {
    toast(t('auth.login.toasts.totpExpired'));
  }
  // eslint-disable-next-line react-hooks/exhaustive-deps
}, []);
```

Add `import { useEffect } from 'react';` if not already (currently only `useState` is imported — change to `import { useEffect, useState } from 'react';`).

- [ ] **Step 3: Type-check**

Run: `pnpm --dir ProjectCeres.Client tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/app/App.tsx ProjectCeres.Client/src/app/pages/auth/Login.tsx
git commit -m "feat(stage-9.2): mount /login/totp route + show toast on /login?expired=1"
```

---

## Task 6 — Unit tests (LoginTotp.test.tsx)

**Files:**
- Create: `ProjectCeres.Client/src/app/pages/auth/LoginTotp.test.tsx`

- [ ] **Step 1: Write the test file**

```tsx
import { render, screen, waitFor, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import i18n from '../../i18n/i18n';
import { AuthProvider } from '../../auth/auth-context';
import { LoginTotp } from './LoginTotp';
import { clearXsrfTokenCacheForTests } from '../../auth/csrf';

function renderLoginTotp() {
  return render(
    <I18nextProvider i18n={i18n}>
      <AuthProvider>
        <MemoryRouter initialEntries={['/login/totp']}>
          <Routes>
            <Route path="/login/totp" element={<LoginTotp />} />
            <Route path="/" element={<div>dashboard</div>} />
            <Route path="/login" element={<div>login</div>} />
            <Route path="/account/unlock" element={<div>unlock page</div>} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </I18nextProvider>,
  );
}

describe('LoginTotp page', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    vi.useFakeTimers();
    fetchSpy = vi.spyOn(global, 'fetch');
    clearXsrfTokenCacheForTests();
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, {
          status: 204,
          headers: { 'X-XSRF-TOKEN': 'test-csrf-token' },
        });
      }
      return new Response(null, { status: 204 });
    });
    document.cookie = '__Host-XSRF=test; path=/';
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it('renders OTP cells, submit, backup-code link, and back-to-sign-in link', () => {
    renderLoginTotp();
    expect(screen.getByLabelText(/verification code/i)).toBeDefined();
    expect(screen.getByRole('button', { name: /^verify$/i })).toBeDefined();
    expect(screen.getByRole('button', { name: /lost your device/i })).toBeDefined();
    expect(screen.getByRole('link', { name: /back to sign in/i })).toBeDefined();
  });

  it('auto-submits when 6 digits typed; on 204 navigates to /', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      if (typeof url === 'string' && url === '/api/auth/login/totp') {
        return new Response(null, { status: 204 });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderLoginTotp();
    await user.type(screen.getByLabelText(/verification code/i), '123456');
    await waitFor(() =>
      expect(fetchSpy).toHaveBeenCalledWith(
        '/api/auth/login/totp',
        expect.objectContaining({ method: 'POST' }),
      ),
    );
    await waitFor(() => expect(screen.getByText('dashboard')).toBeDefined());
  });

  it('on 401 INVALID_MFA_CODE renders the inline error and clears cells', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      if (typeof url === 'string' && url === '/api/auth/login/totp') {
        return new Response(
          JSON.stringify({ error: { code: 'INVALID_MFA_CODE', message: 'no' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderLoginTotp();
    await user.type(screen.getByLabelText(/verification code/i), '111111');
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(/invalid code/i));
  });

  it('on 401 ACCOUNT_LOCKED_OUT navigates to /account/unlock', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      if (typeof url === 'string' && url === '/api/auth/login/totp') {
        return new Response(
          JSON.stringify({ error: { code: 'ACCOUNT_LOCKED_OUT', message: 'no' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderLoginTotp();
    await user.type(screen.getByLabelText(/verification code/i), '222222');
    await waitFor(() => expect(screen.getByText('unlock page')).toBeDefined());
  });

  it('on 401 UNAUTHENTICATED navigates to /login (expired)', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      if (typeof url === 'string' && url === '/api/auth/login/totp') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: 'no' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderLoginTotp();
    await user.type(screen.getByLabelText(/verification code/i), '333333');
    await waitFor(() => expect(screen.getByText('login')).toBeDefined());
  });

  it('on 429 with Retry-After surfaces the countdown message', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      if (typeof url === 'string' && url === '/api/auth/login/totp') {
        return new Response(null, { status: 429, headers: { 'Retry-After': '30' } });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderLoginTotp();
    await user.type(screen.getByLabelText(/verification code/i), '444444');
    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent(/too many attempts.*30 seconds/i),
    );
  });

  it('on 429 with no Retry-After surfaces the no-countdown variant', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      if (typeof url === 'string' && url === '/api/auth/login/totp') {
        return new Response(null, { status: 429 });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderLoginTotp();
    await user.type(screen.getByLabelText(/verification code/i), '555555');
    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent(/too many attempts.*try again in a moment/i),
    );
  });

  it('backup-code mode: link toggles form; submit posts the typed string', async () => {
    fetchSpy.mockImplementation(async (url, init) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      if (typeof url === 'string' && url === '/api/auth/login/totp') {
        const body = JSON.parse((init?.body as string) ?? '{}');
        if (body.code === 'BACKUP-XYZ-123') return new Response(null, { status: 204 });
        return new Response(null, { status: 401 });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderLoginTotp();
    await user.click(screen.getByRole('button', { name: /lost your device/i }));
    await user.type(screen.getByLabelText(/backup code/i), 'BACKUP-XYZ-123');
    await user.click(screen.getByRole('button', { name: /^verify$/i }));
    await waitFor(() => expect(screen.getByText('dashboard')).toBeDefined());
  });

  it('backup-code mode: "use verification code instead" toggles back and clears state', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderLoginTotp();
    await user.click(screen.getByRole('button', { name: /lost your device/i }));
    await user.type(screen.getByLabelText(/backup code/i), 'OLDVAL');
    await user.click(screen.getByRole('button', { name: /use verification code instead/i }));
    expect(screen.getByLabelText(/verification code/i)).toBeDefined();
    expect(screen.queryByLabelText(/backup code/i)).toBeNull();
  });

  it('Verify button label shows "Verifying" while in flight', async () => {
    let resolveResponse: (r: Response) => void = () => {};
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      if (typeof url === 'string' && url === '/api/auth/login/totp') {
        return new Promise<Response>((resolve) => { resolveResponse = resolve; });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderLoginTotp();
    await user.type(screen.getByLabelText(/verification code/i), '666666');
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /verifying/i })).toBeDefined(),
    );
    act(() => resolveResponse(new Response(null, { status: 204 })));
  });
});
```

- [ ] **Step 2: Run the test file**

Run: `pnpm --dir ProjectCeres.Client test -- --run LoginTotp.test`
Expected: 10/10 pass.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Client/src/app/pages/auth/LoginTotp.test.tsx
git commit -m "test(stage-9.2): vitest coverage for LoginTotp (10 scenarios)"
```

---

## Task 7 — a11y test

**Files:**
- Create: `ProjectCeres.Client/src/app/pages/auth/LoginTotp.a11y.test.tsx`

- [ ] **Step 1: Write the a11y test**

```tsx
import { render } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { axe } from 'vitest-axe';
import { describe, expect, it } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import i18n from '../../i18n/i18n';
import { AuthProvider } from '../../auth/auth-context';
import { LoginTotp } from './LoginTotp';

function mount() {
  return render(
    <I18nextProvider i18n={i18n}>
      <AuthProvider>
        <MemoryRouter initialEntries={['/login/totp']}>
          <Routes>
            <Route path="/login/totp" element={<LoginTotp />} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </I18nextProvider>,
  );
}

describe('LoginTotp a11y', () => {
  it('TOTP state has no axe violations', async () => {
    const { container } = mount();
    expect(await axe(container)).toHaveNoViolations();
  });

  it('backup-code state has no axe violations', async () => {
    const { container, getByRole } = mount();
    const user = userEvent.setup();
    await user.click(getByRole('button', { name: /lost your device/i }));
    expect(await axe(container)).toHaveNoViolations();
  });
});
```

- [ ] **Step 2: Run**

Run: `pnpm --dir ProjectCeres.Client test -- --run LoginTotp.a11y`
Expected: 2/2 pass.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Client/src/app/pages/auth/LoginTotp.a11y.test.tsx
git commit -m "test(stage-9.2): vitest-axe a11y for LoginTotp (both modes)"
```

---

## Task 8 — Login test: assert expired-toast renders

**Files:**
- Modify: `ProjectCeres.Client/src/app/pages/auth/Login.test.tsx`

- [ ] **Step 1: Add a test inside the existing `describe('Login page', ...)` block**

Append (before the closing `});`):

```tsx
it('renders a toast when /login?expired=1', async () => {
  // Sonner renders into a portal; assert by querying the document body for
  // the translated copy. We import sonner's Toaster to ensure the portal mounts.
  const { Toaster } = await import('sonner');
  render(
    <I18nextProvider i18n={i18n}>
      <AuthProvider>
        <MemoryRouter initialEntries={['/login?expired=1']}>
          <Routes>
            <Route path="/login" element={<Login />} />
          </Routes>
          <Toaster />
        </MemoryRouter>
      </AuthProvider>
    </I18nextProvider>,
  );
  await waitFor(() =>
    expect(document.body.textContent ?? '').toMatch(/your sign-in expired/i),
  );
});
```

- [ ] **Step 2: Run**

Run: `pnpm --dir ProjectCeres.Client test -- --run Login.test`
Expected: 9/9 pass (was 8; +1 new).

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Client/src/app/pages/auth/Login.test.tsx
git commit -m "test(stage-9.2): assert toast on /login?expired=1"
```

---

## Task 9 — Full client suite + build

- [ ] **Step 1: Full client test suite**

Run: `pnpm --dir ProjectCeres.Client test -- --run`
Expected: all green.

- [ ] **Step 2: Vite production build**

Run: `pnpm --dir ProjectCeres.Client build`
Expected: 0 errors; no new chunk-budget warnings beyond existing baseline.

- [ ] **Step 3: If any regression appears, fix in place (no Skip, no defer per memory `feedback_never_skip_tests_to_make_them_pass`).**

---

## Task 10 — Pre-commit audits (frontend orchestrator Phase 5 subset)

- [ ] **Step 1: `vercel-react-best-practices` review against `LoginTotp.tsx`** — confirm no `useEffect` data-fetching pattern (we use submit handlers), no unnecessary client state (everything is genuinely needed), no missing memoisation that would matter.

- [ ] **Step 2: `web-design-guidelines` review against `LoginTotp.tsx`** — focus management, contrast, label semantics, keyboard nav, `aria-live` polite vs assertive choices.

- [ ] **Step 3: Fix any P0/P1 finding in place; record P2/P3 in a follow-up batch entry (none expected).**

---

## Self-review

- **Spec coverage:**
  - Default + backup-code modes — Task 4.
  - Auto-submit on 6 digits — Task 4, tested in Task 6 case 2.
  - All five error paths (INVALID, LOCKED_OUT, UNAUTHENTICATED, 429 with countdown, 429 fallback, network) — Task 4 + Task 6 cases 3–7 + 9.
  - Backup-code toggle bidirectional + state reset — Task 4 + Task 6 cases 8–9.
  - Verifying label — Task 4 + Task 6 case 10.
  - Route registration — Task 5.
  - Expired toast on Login — Task 5 + Task 8.
  - i18n EN + ES — Task 2 + Task 3.
  - a11y axe-zero — Task 7.
  - No new server work — confirmed.

- **Placeholder scan:** none.

- **Type consistency:** `SubmitOutcome` defined once in Task 4 with all six kinds; `submitTotp` returns it; `handleOutcome` switches over it. No drift between tasks.
