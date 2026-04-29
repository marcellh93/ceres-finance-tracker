import { Sprout } from 'lucide-react';
import { Link } from 'react-router-dom';

type BrandMarkProps = {
  /** When true, hides the wordmark text. Used in rail and on mobile. */
  iconOnly?: boolean;
};

/** Brand mark used in the top bar. Links to the dashboard (/). */
export function BrandMark({ iconOnly = false }: BrandMarkProps) {
  return (
    <Link
      to="/"
      className="flex items-center gap-2 px-4 text-foreground no-underline transition-colors hover:text-primary"
      aria-label="Ceres — go to dashboard"
    >
      <Sprout className="h-6 w-6 text-primary" aria-hidden="true" />
      {!iconOnly && <span className="font-bold tracking-tight">Ceres</span>}
    </Link>
  );
}
