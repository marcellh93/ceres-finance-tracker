import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { LanguageToggle } from '../components/LanguageToggle';
import { openConsentManager } from '../i18n/consent-events';

/**
 * Shared footer for public legal pages: legal links, a button to
 * reopen the cookie-consent banner, and the language toggle.
 */
export function Footer() {
  const { t } = useTranslation();

  return (
    <footer className="border-t px-4 py-6 sm:px-6">
      <nav className="mx-auto flex max-w-[65ch] flex-wrap items-center gap-x-4 gap-y-2 text-sm text-muted-foreground">
        <Link to="/legal#aviso-legal" className="hover:text-foreground">
          {t('legal.footer.avisoLegal')}
        </Link>
        <Link to="/privacy" className="hover:text-foreground">
          {t('legal.footer.privacy')}
        </Link>
        <Link to="/legal#cookies" className="hover:text-foreground">
          {t('legal.footer.cookiePolicy')}
        </Link>
        <Button
          variant="link"
          className="h-auto p-0 text-sm text-muted-foreground hover:text-foreground"
          onClick={openConsentManager}
        >
          {t('legal.footer.manageCookies')}
        </Button>
        <span className="ml-auto">
          <LanguageToggle />
        </span>
      </nav>
    </footer>
  );
}
