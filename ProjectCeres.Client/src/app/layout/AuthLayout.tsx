import { Outlet } from 'react-router-dom';
import { Card } from '@/components/ui/card';
import { BrandMark } from './BrandMark';
import { Footer } from './Footer';
import { ThemeToggle } from '@/design-system/components/ThemeToggle';

/**
 * Layout for unauthenticated auth pages: centered card on a soft
 * neutral background, no sidebar, no top bar. Brand wordmark inside
 * the card; the shared Footer (legal links + manage-cookies + language
 * toggle) sits below the card, with ThemeToggle kept alongside it since
 * Footer doesn't carry one — see Task 8 for the wiring.
 *
 * Responsive: identical at mobile / tablet / desktop per
 * docs/planning-phase3-responsive.md (single-column centered card on
 * every tier).
 */
export function AuthLayout() {
  return (
    <div
      data-consent-reserve
      className="min-h-dvh w-full bg-background flex flex-col items-center justify-center gap-4 p-4 sm:p-6"
    >
      <Card className="w-full max-w-[420px] p-6 sm:p-8 space-y-6">
        <header className="flex flex-col items-center gap-2">
          <BrandMark />
        </header>
        <div>
          <Outlet />
        </div>
      </Card>
      <div className="flex w-full max-w-[420px] items-center justify-between gap-2" data-slot="auth-footer">
        <div className="flex-1">
          <Footer />
        </div>
        <ThemeToggle />
      </div>
    </div>
  );
}
