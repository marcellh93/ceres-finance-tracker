import { Bell, Menu, Plus, Search } from 'lucide-react';
import { useState } from 'react';
import { AvatarMenu } from './AvatarMenu';
import { BrandMark } from './BrandMark';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Kbd } from '@/components/ui/kbd';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { SearchModal } from '../components/SearchModal';
import { useKeyboardShortcut } from '../lib/use-keyboard-shortcut';
import { useMediaQuery } from '../lib/use-media-query';

type TopBarProps = {
  onMenuClick: () => void;
};

export function TopBar({ onMenuClick }: TopBarProps) {
  const [searchOpen, setSearchOpen] = useState(false);
  const [quickAddOpen, setQuickAddOpen] = useState(false);
  const isDesktop = useMediaQuery('(min-width: 640px)');

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
            <Kbd>⌘K</Kbd>
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
        {isDesktop && (
          <>
            <Button
              variant="default"
              size="icon"
              onClick={() => setQuickAddOpen(true)}
              aria-label="Quick add"
            >
              <Plus className="h-4 w-4" />
            </Button>
            <NotificationsButton />
          </>
        )}
        <AvatarMenu />
      </div>

      <SearchModal open={searchOpen} onOpenChange={setSearchOpen} />

      <Dialog open={quickAddOpen} onOpenChange={setQuickAddOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Quick add</DialogTitle>
          </DialogHeader>
          <p className="text-muted-foreground">
            Quick-add will let you record a transaction or transfer without leaving the page. Coming
            in a follow-up plan.
          </p>
        </DialogContent>
      </Dialog>
    </header>
  );
}

function NotificationsButton() {
  return (
    <Popover>
      <PopoverTrigger
        render={
          <Button variant="ghost" size="icon" aria-label="Notifications">
            <Bell className="h-5 w-5" />
          </Button>
        }
      />
      <PopoverContent align="end" className="w-72">
        <p className="text-sm text-muted-foreground">You have no notifications.</p>
      </PopoverContent>
    </Popover>
  );
}
