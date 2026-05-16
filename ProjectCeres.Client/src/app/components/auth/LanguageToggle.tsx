import { Globe } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { writeLangCookie, type SupportedLanguage } from '../../i18n/i18n';

export function LanguageToggle() {
  const { t, i18n } = useTranslation();

  const choose = async (lang: SupportedLanguage) => {
    await i18n.changeLanguage(lang);
    writeLangCookie(lang);
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={
          <Button variant="ghost" size="sm" type="button" aria-label={t('auth.languageToggle.ariaLabel')}>
            <Globe className="h-4 w-4" />
          </Button>
        }
      />
      <DropdownMenuContent align="center">
        <DropdownMenuItem onClick={() => void choose('en')}>
          {t('auth.languageToggle.english')}
        </DropdownMenuItem>
        <DropdownMenuItem onClick={() => void choose('es')}>
          {t('auth.languageToggle.spanish')}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
