import { useRef, useState } from 'react';
import { Outlet } from 'react-router-dom';
import { MobileDrawer } from './MobileDrawer';
import { ReminderCountProvider } from './ReminderCountProvider';
import { Sidebar } from './Sidebar';
import { TopBar } from './TopBar';
import { ReviewCountProvider } from '../features/review/ReviewCountProvider';
import { useMediaQuery } from '../lib/use-media-query';
import { useScrollRestoration } from '../lib/use-scroll-restoration';

export function AppLayout() {
  const [drawerOpen, setDrawerOpen] = useState(false);
  const isDesktop = useMediaQuery('(min-width: 640px)');
  const mainRef = useRef<HTMLElement>(null);
  useScrollRestoration(mainRef);

  return (
    <ReminderCountProvider>
      <ReviewCountProvider>
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
            <main ref={mainRef} id="main-content" className="overflow-y-auto p-4 md:p-6">
              <Outlet />
            </main>
          </div>
          {/* Toaster mounted at App root (App.tsx) so AuthLayout pages also get it. */}
        </div>
      </ReviewCountProvider>
    </ReminderCountProvider>
  );
}
