import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';

type Props = {
  onBack: () => void;
};

export function TotpEnrollStep3Done({ onBack }: Props) {
  const { t } = useTranslation();
  return (
    <div className="space-y-4">
      <h2 className="text-base font-medium">{t('security.totp.wizard.step3.heading')}</h2>

      <div className="space-y-3 text-sm text-muted-foreground">
        <div>
          <div className="font-medium text-foreground">{t('security.totp.wizard.step3.bodyHeading1')}</div>
          <ul className="ml-4 list-disc">
            <li>{t('security.totp.wizard.step3.bodyItem1a')}</li>
            <li>{t('security.totp.wizard.step3.bodyItem1b')}</li>
          </ul>
        </div>
        <div>
          <div className="font-medium text-foreground">{t('security.totp.wizard.step3.bodyHeading2')}</div>
          <ul className="ml-4 list-disc">
            <li>{t('security.totp.wizard.step3.bodyItem2a')}</li>
            <li>{t('security.totp.wizard.step3.bodyItem2b')}</li>
          </ul>
        </div>
      </div>

      <Button type="button" onClick={onBack}>
        {t('security.totp.wizard.step3.back')}
      </Button>
    </div>
  );
}
