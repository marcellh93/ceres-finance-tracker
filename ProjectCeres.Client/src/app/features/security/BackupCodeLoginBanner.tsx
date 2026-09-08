import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { buttonVariants } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { useAuth } from '../../auth/auth-context';

const BACKUP_CODE_BANNER_THRESHOLD = 7;

export function BackupCodeLoginBanner() {
  const { user } = useAuth();
  const { t } = useTranslation();
  const [dismissed, setDismissed] = useState(false);

  if (dismissed) return null;
  if (!user) return null;
  if (!user.twoFactorEnabled) return null;
  if (user.backupCodesRemaining > BACKUP_CODE_BANNER_THRESHOLD) return null;

  const variant: 'reenrol' | 'regenerate' = user.usedBackupCodeAtLastLogin
    ? 'reenrol'
    : 'regenerate';
  const ns = `dashboard.backupCodeBanner.${variant}` as const;

  return (
    <Alert
      onDismiss={() => setDismissed(true)}
      dismissLabel={t('dashboard.backupCodeBanner.dismiss')}
    >
      <AlertDescription>{t(`${ns}.body`, { count: user.backupCodesRemaining })}</AlertDescription>
      <Link
        to="/security"
        className={cn(buttonVariants({ variant: 'link', size: 'sm' }), 'h-auto p-0 no-underline')}
      >
        {t(`${ns}.cta`)}
      </Link>
    </Alert>
  );
}
