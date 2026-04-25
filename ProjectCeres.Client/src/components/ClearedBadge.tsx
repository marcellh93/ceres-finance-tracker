import { useState } from 'react'
import { AlertTriangle, CheckCircle, Clock } from 'lucide-react'

interface ClearedBadgeProps {
  id: string
  type: 'transaction' | 'transfer'
  isCleared: boolean
  needsReview?: boolean
}

export function ClearedBadge({ id, type, isCleared: initialCleared, needsReview = false }: ClearedBadgeProps) {
  const [cleared, setCleared] = useState(initialCleared)

  async function toggle() {
    const next = !cleared
    setCleared(next)

    try {
      const response = await fetch(`/api/movements/${id}/cleared`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ type, cleared: next }),
      })

      if (!response.ok) {
        setCleared(!next)
      }
    } catch {
      setCleared(!next)
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
        <span className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium bg-green-100 text-green-700">
          <CheckCircle size={12} aria-hidden="true" />
          Cleared
        </span>
      ) : needsReview ? (
        <span className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium bg-amber-100 text-amber-700">
          <AlertTriangle size={12} aria-hidden="true" />
          Needs review
        </span>
      ) : (
        <span className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium bg-yellow-100 text-yellow-700">
          <Clock size={12} aria-hidden="true" />
          Pending
        </span>
      )}
    </button>
  )
}
