import { CheckCircle, Clock } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';
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
      className="inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-xs font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
    >
      {cleared ? (
        <span className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium bg-success/10 text-success">
          <CheckCircle size={12} aria-hidden="true" />
          Cleared
        </span>
      ) : (
        <span className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium bg-warning/10 text-warning">
          <Clock size={12} aria-hidden="true" />
          Pending
        </span>
      )}
    </button>
  );
}
