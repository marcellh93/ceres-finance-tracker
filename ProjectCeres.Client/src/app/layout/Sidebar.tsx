import { ChevronsLeft, ChevronsRight } from 'lucide-react';
import { useEffect, useState } from 'react';
import { NavLink } from 'react-router-dom';
import { Button } from '@/components/ui/button';
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip';
import { cn } from '@/lib/utils';
import { bottomItems, navGroups, type NavItem } from './nav-items';
import { readCollapsed, writeCollapsed } from '../lib/sidebar-storage';

export function Sidebar() {
  const [collapsed, setCollapsed] = useState(() => readCollapsed());

  useEffect(() => {
    writeCollapsed(collapsed);
    document.documentElement.classList.toggle('sidebar-collapsed', collapsed);
  }, [collapsed]);

  return (
    <TooltipProvider delay={0}>
      <aside
        aria-label="Sidebar"
        className="flex h-full flex-col border-r border-border bg-background"
      >
        <nav aria-label="Primary" className="flex-1 overflow-y-auto p-3">
          {navGroups.map((group) => (
            <div key={group.label} className="mb-6">
              {!collapsed && (
                <h2 className="mb-2 px-3 text-xs font-medium uppercase tracking-wide text-muted-foreground">
                  {group.label}
                </h2>
              )}
              <ul className="flex flex-col gap-0.5">
                {group.items.map((item) => (
                  <li key={item.to}>
                    <SidebarLink item={item} collapsed={collapsed} />
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </nav>

        <div className="border-t border-border p-3">
          <ul className="mb-2 flex flex-col gap-0.5">
            {bottomItems.map((item) => (
              <li key={item.to}>
                <SidebarLink item={item} collapsed={collapsed} />
              </li>
            ))}
          </ul>
          <Button
            variant="ghost"
            size="sm"
            className="w-full justify-center"
            onClick={() => setCollapsed((c) => !c)}
            aria-expanded={!collapsed}
            aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
          >
            {collapsed ? <ChevronsRight className="h-4 w-4" /> : <ChevronsLeft className="h-4 w-4" />}
          </Button>
        </div>
      </aside>
    </TooltipProvider>
  );
}

function SidebarLink({ item, collapsed }: { item: NavItem; collapsed: boolean }) {
  // `item.useBadge` is a stable hook reference set at module load (see nav-items.ts).
  // Calling it conditionally is safe because the same items render in the same order
  // on every pass — `navGroups` and `bottomItems` are static module-level arrays.
  // eslint-disable-next-line react-hooks/rules-of-hooks
  const badgeCount = item.useBadge?.() ?? 0;
  const ariaLabel = badgeCount > 0 ? `${item.label}, ${badgeCount} pending` : item.label;

  const linkClass = ({ isActive }: { isActive: boolean }) =>
    cn(
      'flex items-center gap-3 rounded-md px-3 py-2 text-sm no-underline transition-colors',
      collapsed && 'justify-center px-2',
      isActive
        ? 'bg-accent text-accent-foreground shadow-[inset_2px_0_0_var(--primary)]'
        : 'text-muted-foreground hover:bg-muted hover:text-foreground',
    );

  const badge =
    badgeCount > 0 ? (
      <span
        data-testid={`nav-badge-${item.to.replace('/', '')}`}
        className="ml-auto rounded-full bg-primary/10 px-2 py-0.5 text-xs"
      >
        {badgeCount}
      </span>
    ) : null;

  const link = (
    <NavLink to={item.to} className={linkClass} end aria-label={ariaLabel}>
      <item.icon className="h-4 w-4 shrink-0" aria-hidden="true" />
      {!collapsed && <span>{item.label}</span>}
      {!collapsed && badge}
    </NavLink>
  );

  if (!collapsed) return link;

  return (
    <Tooltip>
      <TooltipTrigger render={link} />
      <TooltipContent side="right">{ariaLabel}</TooltipContent>
    </Tooltip>
  );
}
