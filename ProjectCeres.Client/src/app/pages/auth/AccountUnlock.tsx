import { useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { readTokenFromHash } from '../../lib/url-hash-token';
import { apiFetch, NetworkError } from '../../lib/api-client';

type State = 'idle' | 'submitting' | 'invalid' | 'network';

export function AccountUnlock() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const token = readTokenFromHash(location.hash);
  const [state, setState] = useState<State>('idle');

  const onUnlock = async () => {
    if (!token) return;
    setState('submitting');
    try {
      const result = await apiFetch('/api/auth/lockout-unlock', {
        method: 'POST',
        body: { token },
      });
      if (result.ok) {
        navigate('/login?unlocked=1');
        return;
      }
      if (result.status === 401) {
        setState('invalid');
        return;
      }
      setState('network');
    } catch (err) {
      if (err instanceof NetworkError) {
        setState('network');
        return;
      }
      setState('network');
    }
  };

  if (!token || state === 'invalid') return <InvalidBlock />;
  if (state === 'network') return <NetworkBlock onRetry={onUnlock} />;

  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold tracking-tight">{t('auth.accountUnlock.title')}</h1>
      <p className="text-sm text-muted-foreground">{t('auth.accountUnlock.description')}</p>
      <Button onClick={onUnlock} disabled={state === 'submitting'} className="w-full">
        {state === 'submitting'
          ? t('auth.accountUnlock.submitting')
          : t('auth.accountUnlock.submit')}
      </Button>
      <div className="text-sm">
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t('auth.accountUnlock.backToSignIn')}
        </Link>
      </div>
    </div>
  );
}

function InvalidBlock() {
  const { t } = useTranslation();
  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold tracking-tight">{t('auth.accountUnlock.invalidTitle')}</h1>
      <p className="text-sm text-muted-foreground" role="alert">{t('auth.accountUnlock.invalidBody')}</p>
      <div className="text-sm">
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t('auth.accountUnlock.backToSignIn')}
        </Link>
      </div>
    </div>
  );
}

function NetworkBlock({ onRetry }: { onRetry: () => void }) {
  const { t } = useTranslation();
  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold tracking-tight">{t('auth.accountUnlock.title')}</h1>
      <p className="text-sm text-destructive" role="alert" aria-live="assertive">
        {t('auth.accountUnlock.errors.network')}
      </p>
      <Button onClick={onRetry} className="w-full">
        {t('auth.accountUnlock.errors.retry')}
      </Button>
      <div className="text-sm">
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t('auth.accountUnlock.backToSignIn')}
        </Link>
      </div>
    </div>
  );
}
