import { ChevronDown, Moon, Sun } from 'lucide-react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Sheet, SheetContent, SheetHeader, SheetTitle } from '@/components/ui/sheet';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { useTheme, type Theme } from '@/app/theme/theme-context';
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
  const preferenceLabel =
    theme === 'system' ? t('theme.system') : theme === 'dark' ? t('theme.dark') : t('theme.light');

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={
          <button
            type="button"
            aria-label={t('theme.ariaLabel')}
            className="mt-2 flex w-full items-center gap-3 rounded-md px-3 py-2 text-sm text-foreground hover:bg-muted"
          >
            <ResolvedIcon className="h-4 w-4 shrink-0" aria-hidden="true" />
            <span>{t('theme.label')}</span>
            <span className="ml-auto flex items-center gap-1 text-xs text-muted-foreground">
              {preferenceLabel}
              <ChevronDown className="h-3.5 w-3.5" aria-hidden="true" />
            </span>
          </button>
        }
      />
      <DropdownMenuContent align="end">
        <DropdownMenuRadioGroup
          value={theme}
          onValueChange={(value) => setTheme(value as Theme)}
        >
          <DropdownMenuRadioItem value="system">{t('theme.system')}</DropdownMenuRadioItem>
          <DropdownMenuRadioItem value="light">{t('theme.light')}</DropdownMenuRadioItem>
          <DropdownMenuRadioItem value="dark">{t('theme.dark')}</DropdownMenuRadioItem>
        </DropdownMenuRadioGroup>
      </DropdownMenuContent>
    </DropdownMenu>
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
