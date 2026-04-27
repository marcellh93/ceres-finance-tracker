import { useEffect, useState } from 'react'
import { BarChart, Bar, XAxis, YAxis, Tooltip, Cell, ResponsiveContainer } from 'recharts'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

interface SpendingSlice {
  categoryName: string
  amount: number
}

const COLORS = ['#6366f1', '#3b82f6', '#22c55e', '#f59e0b', '#ec4899', '#94a3b8']

export function buildDisplayData(data: SpendingSlice[]) {
  const sorted = [...data].sort((a, b) => b.amount - a.amount)
  if (sorted.length <= 5) return sorted.map((d, i) => ({ ...d, color: COLORS[i] }))

  const top5 = sorted.slice(0, 5)
  const otherAmount = sorted.slice(5).reduce((sum, d) => sum + d.amount, 0)
  return [
    ...top5.map((d, i) => ({ ...d, color: COLORS[i] })),
    { categoryName: 'Other', amount: otherAmount, color: COLORS[5] },
  ]
}

export function SpendingByCategoryChart() {
  const [data, setData] = useState<SpendingSlice[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    fetch('/api/dashboard/spending-by-category')
      .then(r => r.json())
      .then((d: SpendingSlice[]) => { setData(d); setLoading(false) })
      .catch(() => setLoading(false))
  }, [])

  const displayData = buildDisplayData(data)
  const height = Math.max(180, displayData.length * 36)

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-base">Spending by Category</CardTitle>
      </CardHeader>
      <CardContent>
        {loading && <p className="text-sm text-muted-foreground">Loading…</p>}
        {!loading && data.length === 0 && (
          <p className="text-sm text-muted-foreground">No expenses this month.</p>
        )}
        {!loading && data.length > 0 && (
          <ResponsiveContainer width="100%" height={height}>
            <BarChart data={displayData} layout="vertical" margin={{ left: 8, right: 24 }}>
              <XAxis type="number" tick={{ fontSize: 11 }} tickFormatter={(v) => `${v.toFixed(0)}`} />
              <YAxis type="category" dataKey="categoryName" tick={{ fontSize: 12 }} width={110} />
              <Tooltip formatter={(value) => Number(value ?? 0).toFixed(2)} />
              <Bar dataKey="amount" name="Amount" radius={[0, 4, 4, 0]}>
                {displayData.map((entry, index) => (
                  <Cell key={index} fill={entry.color} />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        )}
      </CardContent>
    </Card>
  )
}
