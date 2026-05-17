import { Globe } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { writeLangCookie, type SupportedLanguage } from '../i18n/i18n';

export function LanguageToggle() {
  const { t, i18n } = useTranslation();
  // Use i18n.language (the actively set language) rather than resolvedLanguage,
  // which is post-detection resolved and can lag changeLanguage in jsdom.
  // Slice to 2 chars to normalise region variants like 'en-US' → 'en'.
  const current = ((i18n.language ?? 'en').slice(0, 2) as SupportedLanguage);
  const currentCode = t(`auth.languageToggle.code.${current}`);
  const currentName =
    current === 'es' ? t('auth.languageToggle.spanish') : t('auth.languageToggle.english');

  const choose = async (lang: SupportedLanguage) => {
    await i18n.changeLanguage(lang);
    writeLangCookie(lang);
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={
          <Button
            variant="ghost"
            size="sm"
            type="button"
            aria-label={t('auth.languageToggle.ariaLabelWithLanguage', { language: currentName })}
          >
            <Globe className="h-4 w-4" />
            <span className="ml-1.5 text-xs font-medium">{currentCode}</span>
          </Button>
        }
      />
      <DropdownMenuContent align="center">
        <DropdownMenuRadioGroup
          value={current}
          onValueChange={(value) => void choose(value as SupportedLanguage)}
        >
          <DropdownMenuRadioItem value="en">{t('auth.languageToggle.english')}</DropdownMenuRadioItem>
          <DropdownMenuRadioItem value="es">{t('auth.languageToggle.spanish')}</DropdownMenuRadioItem>
        </DropdownMenuRadioGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
