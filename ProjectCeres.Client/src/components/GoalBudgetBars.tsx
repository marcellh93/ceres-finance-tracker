import { useEffect, useState } from 'react'

interface GoalBudgetItem {
  id: string
  name: string
  goalType: string
  amountProgress: number
  targetAmount: number
  percentUsed: number
  currencyCode: string
}

export function GoalBudgetBars() {
  const [items, setItems] = useState<GoalBudgetItem[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    fetch('/api/dashboard/goal-budgets')
      .then(r => r.json())
      .then(data => { setItems(data); setLoading(false) })
      .catch(() => setLoading(false))
  }, [])

  if (loading) return <p>Loading goals…</p>
  if (items.length === 0) return <p>No active goal budgets.</p>

  return (
    <div>
      {items.map(item => (
        <div key={item.id} style={{ marginBottom: '1rem' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between' }}>
            <span>{item.name}</span>
            <span>{item.amountProgress} / {item.targetAmount} {item.currencyCode}</span>
          </div>
          <div style={{ background: '#e5e7eb', borderRadius: '4px', height: '8px', marginTop: '4px' }}>
            <div
              style={{
                width: `${Math.min(item.percentUsed, 100)}%`,
                background: item.percentUsed >= 100 ? '#22c55e' : '#3b82f6',
                height: '100%',
                borderRadius: '4px',
                transition: 'width 0.3s'
              }}
            />
          </div>
          <small>{item.goalType} · {item.percentUsed}% toward goal</small>
        </div>
      ))}
    </div>
  )
}
