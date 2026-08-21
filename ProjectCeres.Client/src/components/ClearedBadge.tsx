import { useState } from 'react'
import { AlertTriangle, CheckCircle, Clock } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { apiFetch } from '@/app/lib/api-client'

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
      const response = await apiFetch(`/api/movements/${id}/cleared`, {
        method: 'PATCH',
        body: { type, cleared: next },
      })

      if (!response.ok) {
        setCleared(!next)
      }
    } catch {
      setCleared(!next)
    }
  }

  const { variant, Icon, label } = cleared
    ? { variant: 'success' as const,   Icon: CheckCircle,   label: 'Cleared' }
    : needsReview
      ? { variant: 'warning' as const, Icon: AlertTriangle, label: 'Needs review' }
      : { variant: 'secondary' as const, Icon: Clock,       label: 'Pending' }

  return (
    <button
      type="button"
      onClick={toggle}
      aria-label={cleared ? 'Mark as pending' : 'Mark as cleared'}
      className="inline-flex items-center rounded focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
    >
      <Badge variant={variant}>
        <Icon size={12} aria-hidden="true" />
        {label}
      </Badge>
    </button>
  )
}
