import type { ReactNode } from 'react';
import { Label } from '@/components/ui/label';

type FieldProps = {
  label: string;
  htmlFor?: string;
  error?: string;
  children: ReactNode;
};

/**
 * Vertical form field: label, control(s), optional inline error.
 *
 * - Pass `htmlFor` when the control has a single `id`; the label associates
 *   to it. Omit `htmlFor` for composite controls (combobox, picker) — the
 *   label renders as plain text with the same look.
 * - Pass `error` to render an inline error below the control.
 */
export function Field({ label, htmlFor, error, children }: FieldProps) {
  return (
    <div className="space-y-1.5">
      {htmlFor ? (
        <Label
          htmlFor={htmlFor}
          className="text-sm font-medium tracking-wide text-foreground/80"
        >
          {label}
        </Label>
      ) : (
        <div className="text-sm font-medium tracking-wide text-foreground/80 select-none">
          {label}
        </div>
      )}
      {children}
      {error && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );
}
