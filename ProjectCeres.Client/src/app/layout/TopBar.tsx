import { Bell, Menu, Plus, Search } from 'lucide-react';
import { useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { AvatarMenu } from './AvatarMenu';
import { BrandMark } from './BrandMark';
import { useReminderCount } from './ReminderCountProvider';
import { ThemeToggle } from '@/design-system/components/ThemeToggle';
import { Button } from '@/components/ui/button';
import { Kbd } from '@/components/ui/kbd';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { Skeleton } from '@/components/ui/skeleton';
import { QuickAddModal } from '../components/QuickAddModal';
import { SearchModal } from '../components/SearchModal';
import { isMac, useKeyboardShortcut } from '../lib/use-keyboard-shortcut';
import { useMediaQuery } from '../lib/use-media-query';

type TopBarProps = {
  onMenuClick: () => void;
};

export function TopBar({ onMenuClick }: TopBarProps) {
  const [searchOpen, setSearchOpen] = useState(false);
  const [quickAddOpen, setQuickAddOpen] = useState(false);
  const isDesktop = useMediaQuery('(min-width: 640px)');
  const location = useLocation();
  const onMovements = /^\/movements(\/|$)/.test(location.pathname);

  useKeyboardShortcut('mod+k', () => setSearchOpen((o) => !o));

  return (
    <header className="flex h-14 items-center border-b border-border bg-background px-4">
      {!isDesktop && (
        <Button
          variant="ghost"
          size="icon"
          onClick={onMenuClick}
          aria-label="Open menu"
          className="mr-2"
        >
          <Menu className="h-5 w-5" />
        </Button>
      )}

      <div
        className="flex items-center"
        style={{ width: isDesktop ? 'var(--sidebar-w, 240px)' : 'auto' }}
      >
        <BrandMark iconOnly={!isDesktop} />
      </div>

      <div className="flex flex-1 items-center justify-center px-4">
        {isDesktop ? (
          <button
            onClick={() => setSearchOpen(true)}
            className="group flex w-full max-w-md items-center gap-2 rounded-md border border-input bg-background px-3 py-1.5 text-sm text-muted-foreground hover:border-ring focus:border-ring focus:outline-none"
            aria-label="Open search"
          >
            <Search className="h-4 w-4" aria-hidden="true" />
            <span className="flex-1 text-left">Search transactions, accounts…</span>
            <Kbd>{isMac() ? '⌘K' : 'Ctrl+K'}</Kbd>
          </button>
        ) : (
          <Button
            variant="ghost"
            size="icon"
            onClick={() => setSearchOpen(true)}
            aria-label="Open search"
            className="ml-auto"
          >
            <Search className="h-5 w-5" />
          </Button>
        )}
      </div>

      <div className="flex items-center gap-1">
        {!onMovements && (
          <Button
            variant="default"
            size="icon"
            onClick={() => setQuickAddOpen(true)}
            aria-label="Quick add"
          >
            <Plus className="h-4 w-4" />
          </Button>
        )}
        {isDesktop && (
          <>
            <NotificationsButton />
            <ThemeToggle />
          </>
        )}
        <AvatarMenu />
      </div>

      <SearchModal open={searchOpen} onOpenChange={setSearchOpen} />

      <QuickAddModal open={quickAddOpen} onOpenChange={setQuickAddOpen} />
    </header>
  );
}

function NotificationsButton() {
  const { count, reminders, loading, refresh } = useReminderCount();
  const [open, setOpen] = useState(false);

  function handleOpenChange(next: boolean) {
    setOpen(next);
    if (next) refresh();
  }

  return (
    <Popover open={open} onOpenChange={handleOpenChange}>
      <PopoverTrigger
        render={
          <Button
            variant="ghost"
            size="icon"
            aria-label={count > 0 ? `Notifications, ${count} due` : 'Notifications'}
            className="relative"
          >
            <Bell className="h-5 w-5" />
            {count > 0 && (
              <span
                aria-hidden
                className="absolute -top-0.5 -right-0.5 flex h-4 w-4 items-center justify-center rounded-full bg-destructive text-[10px] text-white font-semibold"
              >
                {count > 9 ? '9+' : String(count)}
              </span>
            )}
          </Button>
        }
      />
      <PopoverContent align="end" className="w-80 p-0 overflow-hidden">
        {count > 0 ? (
          <div className="bg-destructive/10 border-b border-destructive/20 px-3 py-2.5 flex items-center gap-2">
            <Bell className="h-4 w-4 text-destructive shrink-0" />
            <span className="text-sm font-semibold text-destructive">
              {count === 1 ? '1 reminder due' : `${count} reminders due`}
            </span>
          </div>
        ) : (
          <div className="px-3 py-2.5 border-b text-sm font-medium">Reminders</div>
        )}
        {loading ? (
          <div className="px-3 py-4 space-y-2">
            <Skeleton className="h-4 w-full" />
            <Skeleton className="h-4 w-3/4" />
          </div>
        ) : reminders.length === 0 ? (
          <p className="px-3 py-4 text-sm text-muted-foreground">
            Nothing due. You&apos;re all caught up.
          </p>
        ) : (
          <ul className="max-h-64 overflow-y-auto divide-y divide-border">
            {reminders.map((r) => (
              <li key={r.id}>
                <Link
                  to="/recurring"
                  onClick={() => setOpen(false)}
                  className="flex flex-col gap-0.5 px-3 py-2.5 hover:bg-muted transition-colors"
                >
                  <span className="text-sm font-medium text-foreground leading-snug">{r.name}</span>
                  <span className="text-xs text-destructive font-medium">Due {r.nextDueDate}</span>
                </Link>
              </li>
            ))}
          </ul>
        )}
        <div className="px-3 py-2 border-t bg-muted/40">
          <Button
            nativeButton={false}
            variant="default"
            size="sm"
            className="w-full"
            render={
              <Link to="/recurring" onClick={() => setOpen(false)}>
                Review reminders →
              </Link>
            }
          />
        </div>
      </PopoverContent>
    </Popover>
  );
}
