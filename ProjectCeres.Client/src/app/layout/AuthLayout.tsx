import { Outlet } from 'react-router-dom';
import { Card } from '@/components/ui/card';
import { BrandMark } from './BrandMark';

/**
 * Layout for unauthenticated auth pages: centered card on a soft
 * neutral background, no sidebar, no top bar. Brand wordmark inside
 * the card; the language toggle is mounted as a child of the <footer>
 * element below the Outlet — see Task 6 for the wiring.
 *
 * Responsive: identical at mobile / tablet / desktop per
 * docs/planning-phase3-responsive.md (single-column centered card on
 * every tier).
 */
export function AuthLayout() {
  return (
    <div className="min-h-dvh w-full bg-muted/30 flex items-center justify-center p-4 sm:p-6">
      <Card className="w-full max-w-[420px] p-6 sm:p-8 space-y-6">
        <header className="flex flex-col items-center gap-2">
          <BrandMark />
        </header>
        <div>
          <Outlet />
        </div>
        {/* Footer slot for <LanguageToggle /> — Task 6 adds it as a child of this element.
            The element is kept empty here so Task 6 only needs to add children, not restructure the layout. */}
        <footer className="flex justify-center" data-slot="auth-footer" />
      </Card>
    </div>
  );
}
