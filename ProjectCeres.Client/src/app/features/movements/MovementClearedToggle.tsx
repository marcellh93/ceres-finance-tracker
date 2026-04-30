import { CheckCircle, Clock } from 'lucide-react';
import { useState } from 'react';
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
  const [cleared, setCleared] = useState(initial);

  async function toggle() {
    const next = !cleared;
    setCleared(next);

    try {
      const response = await fetch(MOVEMENTS_CLEARED_URL(id), {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ type: typeForApi(type), cleared: next }),
      });
      if (!response.ok) {
        setCleared(!next);
        toast.error("Couldn't update status.");
      }
    } catch {
      setCleared(!next);
      toast.error("Couldn't update status.");
    }
  }

  return (
    <button
      type="button"
      onClick={toggle}
      aria-label={cleared ? 'Mark as pending' : 'Mark as cleared'}
      className="inline-flex cursor-pointer items-center rounded focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
    >
      {cleared ? (
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
