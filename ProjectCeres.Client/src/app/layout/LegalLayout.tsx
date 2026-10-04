import { Outlet } from 'react-router-dom';
import { BrandMark } from './BrandMark';
import { Footer } from './Footer';

/**
 * Minimal public layout for the legal/policy pages. Neither AuthLayout
 * (420px card) nor AppLayout (authed h-screen shell) fits long-form public
 * text, so these pages get their own column: brand header, scrollable prose,
 * shared footer. No auth dependency — reachable signed-out.
 */
export function LegalLayout() {
  return (
    <div className="min-h-dvh flex flex-col bg-background">
      <header className="border-b px-4 py-4 sm:px-6">
        <BrandMark />
      </header>
      <main className="flex-1 px-4 py-8 sm:px-6">
        <div className="prose-legal mx-auto">
          <Outlet />
        </div>
      </main>
      <Footer />
    </div>
  );
}
