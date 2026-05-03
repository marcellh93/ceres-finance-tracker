import { useState } from 'react';
import { Outlet } from 'react-router-dom';
import { Toaster } from '@/components/ui/sonner';
import { MobileDrawer } from './MobileDrawer';
import { ReminderCountProvider } from './ReminderCountProvider';
import { Sidebar } from './Sidebar';
import { TopBar } from './TopBar';
import { useMediaQuery } from '../lib/use-media-query';

export function AppLayout() {
  const [drawerOpen, setDrawerOpen] = useState(false);
  const isDesktop = useMediaQuery('(min-width: 640px)');

  return (
    <ReminderCountProvider>
      <div className="grid h-screen grid-rows-[3.5rem_1fr] bg-background text-foreground">
        <a
          href="#main-content"
          className="sr-only focus:not-sr-only focus:absolute focus:left-2 focus:top-2 focus:z-50 focus:rounded-md focus:bg-primary focus:px-3 focus:py-1.5 focus:text-primary-foreground"
        >
          Skip to main content
        </a>

        <TopBar onMenuClick={() => setDrawerOpen(true)} />

        <div
          className="grid overflow-hidden"
          style={{ gridTemplateColumns: isDesktop ? 'var(--sidebar-w, 240px) 1fr' : '1fr' }}
        >
          {isDesktop && <Sidebar />}
          {!isDesktop && (
            <MobileDrawer open={drawerOpen} onOpenChange={setDrawerOpen} />
          )}
          <main id="main-content" className="overflow-y-auto p-6">
            <Outlet />
          </main>
        </div>
        <Toaster />
      </div>
    </ReminderCountProvider>
  );
}
