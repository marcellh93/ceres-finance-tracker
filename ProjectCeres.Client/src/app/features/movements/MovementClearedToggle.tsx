import { CheckCircle, Clock } from 'lucide-react';
import { useOptimistic, useRef, useState, useTransition } from 'react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import type { MovementType } from './movements-api';
import { MOVEMENTS_CLEARED_URL } from './movements-api';

type Props = {
  id: string;
  type: MovementType;
  isCleared: boolean;
};

function typeForApi(type: MovementType): string {
  if (type === 'Transaction') return 'transaction';
  if (type === 'Transfer') return 'transfer';
  return 'liabilitypayment';
}

export function MovementClearedToggle({ id, type, isCleared: initial }: Props) {
  const [serverCleared, setServerCleared] = useState(initial);
  const [optimisticCleared, applyOptimistic] = useOptimistic(serverCleared);
  const [, startTransition] = useTransition();
  // Track the latest intended value so rapid clicks toggle from the current
  // optimistic state rather than the (stale) server state.
  const pendingRef = useRef<boolean | null>(null);

  function toggle() {
    // Derive next from the latest in-flight intent (if any) or the server state.
    const current = pendingRef.current !== null ? pendingRef.current : serverCleared;
    const next = !current;
    pendingRef.current = next;

    startTransition(async () => {
      applyOptimistic(next);
      try {
        const response = await fetch(MOVEMENTS_CLEARED_URL(id), {
          method: 'PATCH',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ type: typeForApi(type), cleared: next }),
        });
        if (response.ok) {
          setServerCleared(next);
          // Only clear the ref if this was the last in-flight intent.
          if (pendingRef.current === next) pendingRef.current = null;
        } else {
          toast.error("Couldn't update status.");
        }
      } catch {
        toast.error("Couldn't update status.");
      }
    });
  }

  return (
    <button
      type="button"
      onClick={toggle}
      aria-label={optimisticCleared ? 'Mark as pending' : 'Mark as cleared'}
      className="inline-flex cursor-pointer items-center rounded focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
    >
      {optimisticCleared ? (
        <Badge variant="success">
          <CheckCircle size={12} aria-hidden="true" />
          Cleared
        </Badge>
      ) : (
        <Badge variant="warning">
          <Clock size={12} aria-hidden="true" />
          Pending
        </Badge>
      )}
    </button>
  );
}
