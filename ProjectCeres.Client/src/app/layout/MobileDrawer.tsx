import { Globe, Moon, Sun } from 'lucide-react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Sheet, SheetContent, SheetHeader, SheetTitle } from '@/components/ui/sheet';
import { useTheme, type Theme } from '@/app/theme/theme-context';
import { writeLangCookie, type SupportedLanguage } from '@/app/i18n/i18n';
import { cn } from '@/lib/utils';
import { bottomItems, navGroups, type NavItem } from './nav-items';

type MobileDrawerProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

export function MobileDrawer({ open, onOpenChange }: MobileDrawerProps) {
  const close = () => onOpenChange(false);

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="left" className="w-72 p-0 flex flex-col">
        <SheetHeader className="border-b border-border p-4">
          <SheetTitle>Ceres</SheetTitle>
        </SheetHeader>
        <nav aria-label="Primary" className="flex flex-1 flex-col gap-6 overflow-y-auto p-4">
          {navGroups.map((group) => (
            <div key={group.label}>
              <h2 className="mb-2 text-xs font-medium uppercase tracking-wide text-muted-foreground">
                {group.label}
              </h2>
              <ul className="flex flex-col gap-1">
                {group.items.map((item) => (
                  <li key={item.to}>
                    <DrawerLink item={item} onNavigate={close} />
                  </li>
                ))}
              </ul>
            </div>
          ))}
          <div className="mt-2 border-t border-border pt-4">
            <ul className="flex flex-col gap-1">
              {bottomItems.map((item) => (
                <li key={item.to}>
                  <DrawerLink item={item} onNavigate={close} />
                </li>
              ))}
            </ul>
            <LanguageDrawerRow />
            <ThemeDrawerRow />
          </div>
        </nav>
      </SheetContent>
    </Sheet>
  );
}

function ThemeDrawerRow() {
  const { theme, resolvedTheme, setTheme } = useTheme();
  const { t } = useTranslation();
  const ResolvedIcon = resolvedTheme === 'dark' ? Moon : Sun;

  const options: { value: Theme; label: string }[] = [
    { value: 'system', label: t('theme.system') },
    { value: 'light', label: t('theme.light') },
    { value: 'dark', label: t('theme.dark') },
  ];

  return (
    <div
      role="radiogroup"
      aria-label={t('theme.ariaLabel')}
      className="mt-2 flex items-center gap-3 px-3 py-2"
    >
      <ResolvedIcon className="h-4 w-4 shrink-0 text-foreground" aria-hidden="true" />
      <span className="text-sm text-foreground">{t('theme.label')}</span>
      <div className="ml-auto inline-flex rounded-md border border-border bg-muted/30 p-0.5">
        {options.map(({ value, label }) => {
          const active = theme === value;
          return (
            <button
              key={value}
              type="button"
              role="radio"
              aria-checked={active}
              onClick={() => setTheme(value)}
              className={cn(
                'rounded px-2 py-1 text-xs transition-colors',
                active
                  ? 'bg-background text-foreground shadow-sm'
                  : 'text-muted-foreground hover:text-foreground',
              )}
            >
              {label}
            </button>
          );
        })}
      </div>
    </div>
  );
}

function LanguageDrawerRow() {
  const { i18n, t } = useTranslation();
  const current = ((i18n.language ?? 'en').slice(0, 2) as SupportedLanguage);
  const currentName =
    current === 'es' ? t('auth.languageToggle.spanish') : t('auth.languageToggle.english');

  const options: { value: SupportedLanguage; label: string }[] = [
    { value: 'en', label: t('auth.languageToggle.code.en') },
    { value: 'es', label: t('auth.languageToggle.code.es') },
  ];

  const choose = async (lang: SupportedLanguage) => {
    await i18n.changeLanguage(lang);
    writeLangCookie(lang);
  };

  return (
    <div
      role="radiogroup"
      aria-label={t('auth.languageToggle.ariaLabelWithLanguage', { language: currentName })}
      className="mt-2 flex items-center gap-3 px-3 py-2"
    >
      <Globe className="h-4 w-4 shrink-0 text-foreground" aria-hidden="true" />
      <span className="text-sm text-foreground">{t('auth.languageToggle.label')}</span>
      <div className="ml-auto inline-flex rounded-md border border-border bg-muted/30 p-0.5">
        {options.map(({ value, label }) => {
          const active = current === value;
          return (
            <button
              key={value}
              type="button"
              role="radio"
              aria-checked={active}
              onClick={() => void choose(value)}
              className={cn(
                'rounded px-2 py-1 text-xs transition-colors',
                active
                  ? 'bg-background text-foreground shadow-sm'
                  : 'text-muted-foreground hover:text-foreground',
              )}
            >
              {label}
            </button>
          );
        })}
      </div>
    </div>
  );
}

function DrawerLink({ item, onNavigate }: { item: NavItem; onNavigate: () => void }) {
  return (
    <Link
      to={item.to}
      onClick={onNavigate}
      className="flex items-center gap-3 rounded-md px-3 py-2 text-sm text-foreground no-underline hover:bg-muted"
    >
      <item.icon className="h-4 w-4 shrink-0" aria-hidden="true" />
      <span>{item.label}</span>
    </Link>
  );
}
