import { useEffect, useMemo, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { readTokenFromHash } from '../../lib/url-hash-token';
import { apiFetch } from '../../lib/api-client';

// The server emits this link as {base}/email-change/confirm#token=... — no /app
// prefix, and the token in the FRAGMENT, which browsers never send to the server
// and which stays out of referrer headers and access logs. Same shape as
// EmailVerify; readTokenFromHash is the shared reader.
type State = 'verifying' | 'success' | 'invalid' | 'alreadyInUse';

export function EmailChangeConfirm() {
  const { t } = useTranslation();
  const location = useLocation();
  const token = useMemo(() => readTokenFromHash(location.hash), [location.hash]);
  const [state, setState] = useState<State>(token ? 'verifying' : 'invalid');

  useEffect(() => {
    if (state !== 'verifying' || !token) return;
    let cancelled = false;
    (async () => {
      try {
        const result = await apiFetch('/api/auth/email-change/confirm', {
          method: 'POST',
          body: { token },
        });
        if (cancelled) return;
        if (result.ok) {
          setState('success');
          return;
        }
        // The address was taken by someone else between request and confirm. The
        // server leaves the token unconsumed in this case, but there is nothing
        // useful the user can do here beyond starting over, so we say so plainly.
        setState(result.code === 'EMAIL_ALREADY_IN_USE' ? 'alreadyInUse' : 'invalid');
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
        <h1 className="text-xl font-semibold tracking-tight">
          {t('auth.emailChangeConfirm.title')}
        </h1>
        <p className="text-sm text-muted-foreground" role="status" aria-live="polite">
          {t('auth.emailChangeConfirm.verifying')}
        </p>
      </div>
    );
  }

  if (state === 'success') {
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold tracking-tight">
          {t('auth.emailChangeConfirm.successTitle')}
        </h1>
        <p className="text-sm text-muted-foreground">
          {t('auth.emailChangeConfirm.successBody')}
        </p>
        <div className="text-sm">
          <Link to="/login" className="underline-offset-4 hover:underline">
            {t('auth.emailChangeConfirm.signInLink')}
          </Link>
        </div>
      </div>
    );
  }

  const isTaken = state === 'alreadyInUse';
  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold tracking-tight">
        {t(isTaken ? 'auth.emailChangeConfirm.alreadyInUseTitle' : 'auth.emailChangeConfirm.invalidTitle')}
      </h1>
      <p className="text-sm text-muted-foreground" role="alert">
        {t(isTaken ? 'auth.emailChangeConfirm.alreadyInUseBody' : 'auth.emailChangeConfirm.invalidBody')}
      </p>
      <div className="text-sm">
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t('auth.emailChangeConfirm.signInLink')}
        </Link>
      </div>
    </div>
  );
}
