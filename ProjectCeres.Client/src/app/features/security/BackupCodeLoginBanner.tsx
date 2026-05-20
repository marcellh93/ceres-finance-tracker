import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { AlertTriangle, X } from 'lucide-react';
import { Button, buttonVariants } from '@/components/ui/button';
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
    <div
      role="status"
      aria-live="polite"
      className="flex items-start gap-3 rounded-md border border-warning/30 bg-warning/10 p-4 text-sm"
    >
      <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-warning" aria-hidden />
      <div className="flex-1 space-y-2">
        <p>{t(`${ns}.body`, { count: user.backupCodesRemaining })}</p>
        <Link
          to="/app/security"
          className={cn(buttonVariants({ variant: 'link', size: 'sm' }), 'h-auto p-0 no-underline')}
        >
          {t(`${ns}.cta`)}
        </Link>
      </div>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        className="-mr-2 -mt-1 h-7 w-7 shrink-0"
        onClick={() => setDismissed(true)}
        aria-label={t('dashboard.backupCodeBanner.dismiss')}
      >
        <X className="h-4 w-4" />
      </Button>
    </div>
  );
}
