import { Moon, Sun } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { useTheme } from '@/app/theme/theme-context';

type ThemeToggleProps = {
  showLabel?: boolean;
};

export function ThemeToggle({ showLabel = false }: ThemeToggleProps) {
  const { resolvedTheme, setTheme } = useTheme();

  const isDark = resolvedTheme === 'dark';
  const nextTheme = isDark ? 'light' : 'dark';
  const ariaLabel = isDark ? 'Switch to light mode' : 'Switch to dark mode';

  if (showLabel) {
    return (
      <Button
        variant="outline"
        size="sm"
        onClick={() => setTheme(nextTheme)}
        aria-label={ariaLabel}
      >
        {isDark ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
        <span className="ml-2">{isDark ? 'Light' : 'Dark'}</span>
      </Button>
    );
  }

  return (
    <Button
      variant="ghost"
      size="icon"
      onClick={() => setTheme(nextTheme)}
      aria-label={ariaLabel}
    >
      {isDark ? <Sun className="h-5 w-5" /> : <Moon className="h-5 w-5" />}
    </Button>
  );
}
