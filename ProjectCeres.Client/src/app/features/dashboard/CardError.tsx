import { AlertTriangle } from 'lucide-react';
import { Button } from '@/components/ui/button';

type CardErrorProps = {
  /** What couldn't load. Used in the message: "Couldn't load <section>." */
  section: string;
  onRetry: () => void;
};

export function CardError({ section, onRetry }: CardErrorProps) {
  return (
    <div className="flex flex-col items-start gap-3 py-2">
      <p className="flex items-center gap-2 text-sm text-muted-foreground">
        <AlertTriangle className="h-4 w-4 text-destructive" aria-hidden="true" />
        Couldn't load {section}.
      </p>
      <Button variant="outline" size="sm" onClick={onRetry}>
        Retry
      </Button>
    </div>
  );
}
