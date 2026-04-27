import { useEffect, useState } from 'react'
import { BarChart, Bar, XAxis, YAxis, Tooltip, ReferenceLine, Cell, ResponsiveContainer } from 'recharts'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

interface CashFlowPoint {
  month: string
  netFlow: number
}

export function CashFlowChart() {
  const [data, setData] = useState<CashFlowPoint[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    fetch('/api/dashboard/cash-flow')
      .then(r => r.json())
      .then((d: CashFlowPoint[]) => { setData(d); setLoading(false) })
      .catch(() => setLoading(false))
  }, [])

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-base">Monthly Cash Flow</CardTitle>
      </CardHeader>
      <CardContent>
        {loading && <p className="text-sm text-muted-foreground">Loading…</p>}
        {!loading && data.length === 0 && (
          <p className="text-sm text-muted-foreground">No data available.</p>
        )}
        {!loading && data.length > 0 && (
          <ResponsiveContainer width="100%" height={220}>
            <BarChart data={data}>
              <XAxis dataKey="month" tick={{ fontSize: 12 }} />
              <YAxis tick={{ fontSize: 12 }} />
              <Tooltip formatter={(value: number) => value.toFixed(2)} />
              <ReferenceLine y={0} stroke="#94a3b8" />
              <Bar dataKey="netFlow" name="Net Flow" radius={[4, 4, 0, 0]}>
                {data.map((item, index) => (
                  <Cell key={index} fill={item.netFlow >= 0 ? '#22c55e' : '#ef4444'} />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        )}
      </CardContent>
    </Card>
  )
}
