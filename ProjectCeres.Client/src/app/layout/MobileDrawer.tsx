import { Link } from 'react-router-dom';
import { Sheet, SheetContent, SheetHeader, SheetTitle } from '@/components/ui/sheet';
import { bottomItems, navGroups, type NavItem } from './nav-items';

type MobileDrawerProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

export function MobileDrawer({ open, onOpenChange }: MobileDrawerProps) {
  const close = () => onOpenChange(false);

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="left" className="w-72 p-0">
        <SheetHeader className="border-b border-border p-4">
          <SheetTitle>Ceres</SheetTitle>
        </SheetHeader>
        <nav aria-label="Primary" className="flex flex-col gap-6 p-4">
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
          </div>
        </nav>
      </SheetContent>
    </Sheet>
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
