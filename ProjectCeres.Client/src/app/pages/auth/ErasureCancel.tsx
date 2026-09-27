import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { CheckCircle2, Ban, TriangleAlert } from 'lucide-react';
import { readTokenFromHash } from '../../lib/url-hash-token';
import { apiFetch } from '../../lib/api-client';

// Emitted as {base}/erasure/cancel#token=... by GdprErasureInitiated (no /app
// prefix, token in the fragment, matching EmailChangeRevoke's shape). Anonymous
// by design: a sealed account cannot authenticate, so the token itself is the
// credential (ProfileApiController.CancelErasure, [AllowAnonymous]). Auto-fires
// on load rather than requiring a click — the point of this link is to be the
// fastest possible undo for someone who changed their mind, or panicked.
type State = 'verifying' | 'cancelled' | 'invalid' | 'gone';

// Each of the four states renders its own <h1> (a different DOM node, since
// the states are mutually exclusive early returns) — the effect must re-fire
// per state, not just on the component's initial mount, or the outcome
// heading (cancelled/gone/invalid) never receives focus after the transition
// out of "verifying".
function useHeadingFocus(state: State) {
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    headingRef.current?.focus();
  }, [state]);
  return headingRef;
}

export function ErasureCancel() {
  const { t } = useTranslation();
  const location = useLocation();
  const token = useMemo(() => readTokenFromHash(location.hash), [location.hash]);
  const [state, setState] = useState<State>(token ? 'verifying' : 'invalid');
  const headingRef = useHeadingFocus(state);

  useEffect(() => {
    if (state !== 'verifying' || !token) return;
    let cancelled = false;
    (async () => {
      try {
        const result = await apiFetch('/api/profile/erasure/cancel', {
          method: 'POST',
          body: { token },
        });
        if (cancelled) return;
        if (result.ok) {
          setState('cancelled');
          return;
        }
        setState(result.status === 410 ? 'gone' : 'invalid');
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
        <h1 ref={headingRef} tabIndex={-1} className="text-xl font-semibold tracking-tight outline-none">
          {t('auth.erasureCancel.title')}
        </h1>
        <p className="text-sm text-muted-foreground" role="status" aria-live="polite">
          {t('auth.erasureCancel.verifying')}
        </p>
      </div>
    );
  }

  if (state === 'cancelled') {
    return (
      <div className="space-y-4">
        <div className="flex items-start gap-3 rounded-md border border-success/30 bg-success/10 p-4">
          <CheckCircle2 className="mt-0.5 size-5 shrink-0 text-success" aria-hidden="true" />
          <div className="space-y-1.5" role="status" aria-live="polite">
            <h1 ref={headingRef} tabIndex={-1} className="text-base font-medium text-success outline-none">
              {t('auth.erasureCancel.cancelledTitle')}
            </h1>
            <p className="text-sm text-foreground/80">{t('auth.erasureCancel.cancelledBody')}</p>
          </div>
        </div>
        <div className="text-sm">
          <Link to="/login" className="underline-offset-4 hover:underline">
            {t('auth.erasureCancel.signInLink')}
          </Link>
        </div>
      </div>
    );
  }

  if (state === 'gone') {
    return (
      <div className="space-y-4">
        <div className="flex items-start gap-3 rounded-md border border-destructive/30 bg-destructive/10 p-4">
          <Ban className="mt-0.5 size-5 shrink-0 text-destructive" aria-hidden="true" />
          <div className="space-y-1.5" role="alert">
            <h1 ref={headingRef} tabIndex={-1} className="text-base font-medium text-destructive outline-none">
              {t('auth.erasureCancel.goneTitle')}
            </h1>
            <p className="text-sm text-foreground/80">{t('auth.erasureCancel.goneBody')}</p>
          </div>
        </div>
        <div className="text-sm">
          <Link to="/login" className="underline-offset-4 hover:underline">
            {t('auth.erasureCancel.signInLink')}
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-start gap-3 rounded-md border border-warning/30 bg-warning/10 p-4">
        <TriangleAlert className="mt-0.5 size-5 shrink-0 text-warning-foreground" aria-hidden="true" />
        <div className="space-y-1.5" role="alert">
          <h1 ref={headingRef} tabIndex={-1} className="text-base font-medium text-warning-foreground outline-none">
            {t('auth.erasureCancel.invalidTitle')}
          </h1>
          <p className="text-sm text-foreground/80">{t('auth.erasureCancel.invalidBody')}</p>
        </div>
      </div>
      <div className="text-sm">
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t('auth.erasureCancel.signInLink')}
        </Link>
      </div>
    </div>
  );
}
