import { useEffect, useState } from 'react'

interface CategoryBudgetItem {
  id: string
  categoryName: string
  currencyCode: string
  spent: number
  limit: number
  percentUsed: number
}

export function CategoryBudgetBars() {
  const [items, setItems] = useState<CategoryBudgetItem[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    fetch('/api/dashboard/category-budgets')
      .then(r => r.json())
      .then(data => { setItems(data); setLoading(false) })
      .catch(() => setLoading(false))
  }, [])

  if (loading) return <p>Loading budgets…</p>
  if (items.length === 0) return <p>No active category budgets.</p>

  return (
    <div>
      {items.map(item => (
        <div key={item.id} style={{ marginBottom: '1rem' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between' }}>
            <span>{item.categoryName}</span>
            <span>{item.spent} / {item.limit} {item.currencyCode}</span>
          </div>
          <div style={{ background: '#e5e7eb', borderRadius: '4px', height: '8px', marginTop: '4px' }}>
            <div
              style={{
                width: `${Math.min(item.percentUsed, 100)}%`,
                background: item.percentUsed >= 100 ? '#ef4444' : item.percentUsed >= 80 ? '#f59e0b' : '#22c55e',
                height: '100%',
                borderRadius: '4px',
                transition: 'width 0.3s'
              }}
            />
          </div>
          <small>{item.percentUsed}% used</small>
        </div>
      ))}
    </div>
  )
}
