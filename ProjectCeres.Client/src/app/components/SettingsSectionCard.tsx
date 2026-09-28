import type { ReactNode } from 'react';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { cn } from '@/lib/utils';

type SettingsSectionCardProps = {
  title: string;
  description: string;
  /** One button, several side by side, or a dialog-trigger component. */
  children: ReactNode;
  /** `destructive` tints the whole card for a danger-zone-style section. */
  tone?: 'default' | 'destructive';
  className?: string;
};

/**
 * The heading + description + action-area card repeated across every Settings
 * surface (Account's export/danger-zone cards, Security's MFA section).
 * Extracted Stage 13.9 close-out — three independent hand-rolled copies had
 * already drifted (Security's version wasn't even Card-based).
 */
export function SettingsSectionCard({
  title,
  description,
  children,
  tone = 'default',
  className,
}: SettingsSectionCardProps) {
  return (
    <Card
      className={cn(tone === 'destructive' && 'border-destructive/30 bg-destructive/10', className)}
    >
      <CardHeader>
        <CardTitle className={cn('text-base font-medium', tone === 'destructive' && 'text-destructive')}>
          {title}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <CardDescription className={cn('text-sm', tone === 'destructive' && 'text-destructive/90')}>
          {description}
        </CardDescription>
        <div className="flex flex-wrap gap-3">{children}</div>
      </CardContent>
    </Card>
  );
}
