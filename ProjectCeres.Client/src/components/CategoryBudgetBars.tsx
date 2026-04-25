import { useEffect, useState } from 'react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Progress } from '@/components/ui/progress'
import { Badge } from '@/components/ui/badge'

interface CategoryBudgetItem {
  id: string
  categoryName: string
  currencyCode: string
  spent: number
  limit: number
  percentUsed: number
}

function statusVariant(pct: number): 'default' | 'secondary' | 'destructive' {
  if (pct >= 100) return 'destructive'
  if (pct >= 80) return 'secondary'
  return 'default'
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

  if (loading) return <p className="text-sm text-muted-foreground">Loading budgets…</p>
  if (items.length === 0) return <p className="text-sm text-muted-foreground">No active category budgets.</p>

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-base">Category Budgets</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {items.map(item => (
          <div key={item.id} className="space-y-1.5">
            <div className="flex items-center justify-between text-sm">
              <span className="font-medium">{item.categoryName}</span>
              <div className="flex items-center gap-2">
                <span className="text-muted-foreground">
                  {item.spent.toFixed(2)} / {item.limit.toFixed(2)} {item.currencyCode}
                </span>
                <Badge variant={statusVariant(item.percentUsed)} className="text-xs">
                  {item.percentUsed}%
                </Badge>
              </div>
            </div>
            <Progress value={Math.min(item.percentUsed, 100)} className="h-2" />
          </div>
        ))}
      </CardContent>
    </Card>
  )
}
