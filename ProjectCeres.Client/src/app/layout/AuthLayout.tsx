import { Outlet } from 'react-router-dom';
import { Card } from '@/components/ui/card';
import { BrandMark } from './BrandMark';

/**
 * Layout for unauthenticated auth pages: centered card on a soft
 * neutral background, no sidebar, no top bar. Brand wordmark inside
 * the card; the language toggle slot in the footer is filled by
 * <LanguageToggle /> which is mounted in the Outlet's siblings —
 * see Task 6 for the full mounting.
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
        {/* Footer slot for <LanguageToggle /> — filled in Task 6.
            Empty <footer> kept here so axe sees a landmark. */}
        <footer className="flex justify-center" data-slot="auth-footer" />
      </Card>
    </div>
  );
}
