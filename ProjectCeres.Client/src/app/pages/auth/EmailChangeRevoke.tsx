import { useEffect, useMemo, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { readTokenFromHash } from '../../lib/url-hash-token';
import { apiFetch } from '../../lib/api-client';

// Emitted as {base}/email-change/revoke#token=... (no /app prefix, token in the
// fragment). This is the security half of the flow: the notice goes to the OLD
// address, so the person clicking may not be the person who asked for the change
// — and often will not be signed in. Anonymous by design.
type State = 'verifying' | 'success' | 'invalid';

export function EmailChangeRevoke() {
  const { t } = useTranslation();
  const location = useLocation();
  const token = useMemo(() => readTokenFromHash(location.hash), [location.hash]);
  const [state, setState] = useState<State>(token ? 'verifying' : 'invalid');

  useEffect(() => {
    if (state !== 'verifying' || !token) return;
    let cancelled = false;
    (async () => {
      try {
        const result = await apiFetch('/api/auth/email-change/revoke', {
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
        <h1 className="text-xl font-semibold tracking-tight">
          {t('auth.emailChangeRevoke.title')}
        </h1>
        <p className="text-sm text-muted-foreground" role="status" aria-live="polite">
          {t('auth.emailChangeRevoke.verifying')}
        </p>
      </div>
    );
  }

  if (state === 'success') {
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold tracking-tight">
          {t('auth.emailChangeRevoke.successTitle')}
        </h1>
        <p className="text-sm text-muted-foreground">
          {t('auth.emailChangeRevoke.successBody')}
        </p>
        <div className="text-sm">
          <Link to="/password-reset" className="underline-offset-4 hover:underline">
            {t('auth.emailChangeRevoke.passwordResetLink')}
          </Link>
        </div>
      </div>
    );
  }

  // A dead revoke link is NOT just "invalid". The cancel link lasts 7 days but the
  // confirmation link only 30 minutes, so this link routinely outlives the change it
  // was meant to stop — meaning the change may ALREADY have gone through, and the
  // person reading this is exactly who that would harm. Saying only "invalid link"
  // would leave them with no idea what happened or what to do. RevokeAsync cannot
  // undo a completed change (it only consumes tokens still in flight), so the honest
  // move is to name both possibilities and give them the two steps that still help.
  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold tracking-tight">
        {t('auth.emailChangeRevoke.invalidTitle')}
      </h1>
      <p className="text-sm text-muted-foreground" role="alert">
        {t('auth.emailChangeRevoke.invalidBody')}
      </p>
      <div className="space-y-2 text-sm">
        <p className="font-medium">{t('auth.emailChangeRevoke.invalidActionIntro')}</p>
        <ol className="list-decimal space-y-1 pl-5 text-muted-foreground">
          <li>
            <Link to="/password-reset" className="underline-offset-4 hover:underline">
              {t('auth.emailChangeRevoke.invalidActionReset')}
            </Link>
          </li>
          <li>{t('auth.emailChangeRevoke.invalidActionSupport')}</li>
        </ol>
      </div>
      <div className="text-sm">
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t('auth.emailChangeRevoke.signInLink')}
        </Link>
      </div>
    </div>
  );
}
