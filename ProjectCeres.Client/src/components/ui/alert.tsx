import { type ComponentProps, type ReactNode } from 'react';
import { AlertTriangle, X, type LucideIcon } from 'lucide-react';
import { cva, type VariantProps } from 'class-variance-authority';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';

/**
 * Surface-level nudge — more presence than a `<Badge>`, less than a `<CardError>`.
 * Promoted from the "Inline warning strip" recipe at the fifth caller (Stage 12.5.3
 * shipped the fifth; see docs/design-system.md § Alert). Composable: pass the message
 * (and any CTA) as children, wrapped in `<AlertTitle>` / `<AlertDescription>` as needed.
 *
 * `variant="warning"` uses the amber palette: `border-warning/30 bg-warning/10` surround,
 * the icon stroke in `text-warning` (AA-Large only), and body copy in `text-warning-foreground`
 * (the paired AA token). Only `warning` exists today — the cva is the seam for future variants.
 */
const alertVariants = cva(
  'relative flex items-start gap-3 rounded-md border p-4 text-sm',
  {
    variants: {
      variant: {
        warning: 'border-warning/30 bg-warning/10 text-warning-foreground',
      },
    },
    defaultVariants: { variant: 'warning' },
  },
);

const variantIcon: Record<NonNullable<VariantProps<typeof alertVariants>['variant']>, LucideIcon> = {
  warning: AlertTriangle,
};

const variantIconColor: Record<
  NonNullable<VariantProps<typeof alertVariants>['variant']>,
  string
> = {
  warning: 'text-warning',
};

type AlertProps = ComponentProps<'div'> &
  VariantProps<typeof alertVariants> & {
    /** Override the default per-variant icon; pass `null` to render no icon. */
    icon?: LucideIcon | null;
    /** When provided, renders a dismiss (X) button; the label is required for a11y. */
    onDismiss?: () => void;
    dismissLabel?: string;
    children: ReactNode;
  };

export function Alert({
  className,
  variant = 'warning',
  icon,
  onDismiss,
  dismissLabel,
  children,
  ...props
}: AlertProps) {
  const v = variant ?? 'warning';
  const Icon = icon === null ? null : (icon ?? variantIcon[v]);

  return (
    <div
      role="status"
      aria-live="polite"
      className={cn(alertVariants({ variant: v }), className)}
      {...props}
    >
      {Icon && <Icon className={cn('mt-0.5 h-5 w-5 shrink-0', variantIconColor[v])} aria-hidden />}
      <div className="flex-1 space-y-2">{children}</div>
      {onDismiss && (
        <Button
          type="button"
          variant="ghost"
          size="icon"
          className="-mr-2 -mt-1 size-11 shrink-0"
          onClick={onDismiss}
          aria-label={dismissLabel}
        >
          <X className="h-4 w-4" />
        </Button>
      )}
    </div>
  );
}

/** Emphasised first line inside an `<Alert>` (the recipe's `font-medium` heading). */
export function AlertTitle({ className, ...props }: ComponentProps<'p'>) {
  return <p className={cn('font-medium', className)} {...props} />;
}

/** Body copy inside an `<Alert>`. */
export function AlertDescription({ className, ...props }: ComponentProps<'p'>) {
  return <p className={className} {...props} />;
}
