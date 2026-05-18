import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { toast } from 'sonner';
import { Copy, Download, AlertTriangle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { downloadBackupCodes } from './backup-codes-download';

type Props = {
  codes: string[];
  userEmail: string;
  onDone: () => void;
  onCancelWithoutSaving: () => void;
};

export function TotpEnrollStep2BackupCodes({ codes, userEmail, onDone, onCancelWithoutSaving }: Props) {
  const { t } = useTranslation();
  const [confirmed, setConfirmed] = useState(false);
  const [cancelDialogOpen, setCancelDialogOpen] = useState(false);

  const copyAll = async () => {
    try {
      await navigator.clipboard.writeText(codes.join('\n'));
      toast.success(t('security.totp.wizard.step2.copiedToast'));
    } catch {
      /* silent — clipboard permission denied; user can use Download instead */
    }
  };

  const handleDownload = () => {
    downloadBackupCodes(codes, userEmail, t('security.totp.wizard.step2.downloadFilename'));
  };

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <h2 className="text-base font-medium">{t('security.totp.wizard.step2.heading')}</h2>
        <p className="text-sm text-muted-foreground">{t('security.totp.wizard.step2.body')}</p>
      </div>

      <ul
        aria-label={t('security.totp.wizard.step2.heading')}
        className="grid grid-cols-1 gap-2 sm:grid-cols-2"
      >
        {codes.map((code) => (
          <li
            key={code}
            className="rounded-md border border-border bg-muted/30 px-3 py-2 font-mono text-sm"
          >
            {code}
          </li>
        ))}
      </ul>

      <div className="flex flex-wrap gap-2">
        <Button type="button" variant="outline" onClick={copyAll}>
          <Copy className="h-4 w-4" />
          {t('security.totp.wizard.step2.copyAll')}
        </Button>
        <Button type="button" variant="outline" onClick={handleDownload}>
          <Download className="h-4 w-4" />
          {t('security.totp.wizard.step2.download')}
        </Button>
      </div>

      <div className="flex items-start gap-2 rounded-md border border-warning/30 bg-warning/10 p-3 text-sm">
        <AlertTriangle className="mt-0.5 h-4 w-4 text-warning" aria-hidden />
        <span>{t('security.totp.wizard.step2.warning')}</span>
      </div>

      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          checked={confirmed}
          onChange={(e) => setConfirmed(e.target.checked)}
          aria-label={t('security.totp.wizard.step2.confirmCheckboxLabel')}
          className="h-4 w-4 rounded border-border accent-primary"
        />
        {t('security.totp.wizard.step2.confirmCheckboxLabel')}
      </label>

      <div className="flex justify-between">
        <Button type="button" variant="link" onClick={() => setCancelDialogOpen(true)} className="px-0">
          {t('security.totp.wizard.cancel')}
        </Button>
        <Button type="button" disabled={!confirmed} onClick={onDone}>
          {t('security.totp.wizard.step2.done')}
        </Button>
      </div>

      <AlertDialog open={cancelDialogOpen} onOpenChange={setCancelDialogOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('security.totp.wizard.step2.cancelAlert.title')}</AlertDialogTitle>
            <AlertDialogDescription>
              {t('security.totp.wizard.step2.cancelAlert.body')}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('security.totp.wizard.step2.cancelAlert.cancel')}</AlertDialogCancel>
            <AlertDialogAction onClick={onCancelWithoutSaving}>
              {t('security.totp.wizard.step2.cancelAlert.confirm')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
