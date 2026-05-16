import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';

export function PasswordResetPlaceholder() {
  const { t } = useTranslation();
  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold tracking-tight">
        {t('auth.placeholders.passwordReset.title')}
      </h1>
      <p className="text-sm text-muted-foreground">
        {t('auth.placeholders.passwordReset.body')}
      </p>
      <div className="text-sm">
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t('auth.placeholders.passwordReset.backToLogin')}
        </Link>
      </div>
    </div>
  );
}
