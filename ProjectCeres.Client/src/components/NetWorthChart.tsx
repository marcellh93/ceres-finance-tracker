import { useEffect, useState } from 'react'
import { LineChart, Line, XAxis, YAxis, Tooltip, Legend, ResponsiveContainer } from 'recharts'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

interface NetWorthPoint {
  month: string
  assets: number
  liabilities: number
  netWorth: number
}

export function NetWorthChart() {
  const [data, setData] = useState<NetWorthPoint[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    fetch('/api/dashboard/net-worth-trend')
      .then(r => r.json())
      .then((d: NetWorthPoint[]) => { setData(d); setLoading(false) })
      .catch(() => setLoading(false))
  }, [])

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-base">Net Worth Over Time</CardTitle>
      </CardHeader>
      <CardContent>
        {loading && <p className="text-sm text-muted-foreground">Loading…</p>}
        {!loading && data.length === 0 && (
          <p className="text-sm text-muted-foreground">No data available.</p>
        )}
        {!loading && data.length > 0 && (
          <ResponsiveContainer width="100%" height={220}>
            <LineChart data={data}>
              <XAxis dataKey="month" tick={{ fontSize: 12 }} />
              <YAxis tick={{ fontSize: 12 }} />
              <Tooltip formatter={(value: number) => value.toFixed(2)} />
              <Legend />
              <Line type="monotone" dataKey="assets" stroke="#3b82f6" name="Assets" dot={false} strokeWidth={2} />
              <Line type="monotone" dataKey="netWorth" stroke="#22c55e" name="Net Worth" dot={false} strokeWidth={2} />
            </LineChart>
          </ResponsiveContainer>
        )}
      </CardContent>
    </Card>
  )
}
