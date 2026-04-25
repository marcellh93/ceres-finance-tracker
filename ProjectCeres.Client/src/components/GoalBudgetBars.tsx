import { useEffect, useState } from 'react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Progress } from '@/components/ui/progress'
import { Badge } from '@/components/ui/badge'

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

  if (loading) return <p className="text-sm text-muted-foreground">Loading goals…</p>
  if (items.length === 0) return <p className="text-sm text-muted-foreground">No active goal budgets.</p>

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-base">Goal Budgets</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {items.map(item => (
          <div key={item.id} className="space-y-1.5">
            <div className="flex items-center justify-between text-sm">
              <div className="flex items-center gap-2">
                <span className="font-medium">{item.name}</span>
                <span className="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium bg-blue-100 text-blue-800">{item.goalType}</span>
              </div>
              <span className="text-muted-foreground">
                {item.amountProgress.toFixed(2)} / {item.targetAmount.toFixed(2)} {item.currencyCode}
              </span>
            </div>
            <Progress value={Math.min(item.percentUsed, 100)} className="h-2" />
            <p className="text-xs text-muted-foreground">{item.percentUsed}% toward goal</p>
          </div>
        ))}
      </CardContent>
    </Card>
  )
}
