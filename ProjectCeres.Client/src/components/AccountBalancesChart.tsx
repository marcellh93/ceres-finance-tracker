import { useEffect, useState } from 'react'
import { BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, Cell } from 'recharts'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

interface AccountBalance {
  accountName: string
  balance: number
}

export function AccountBalancesChart() {
  const [data, setData] = useState<AccountBalance[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    fetch('/api/dashboard/account-balances')
      .then(r => r.json())
      .then((d: AccountBalance[]) => { setData(d); setLoading(false) })
      .catch(() => setLoading(false))
  }, [])

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-base">Account Balances</CardTitle>
      </CardHeader>
      <CardContent>
        {loading && <p className="text-sm text-muted-foreground">Loading…</p>}
        {!loading && data.length === 0 && (
          <p className="text-sm text-muted-foreground">No accounts found.</p>
        )}
        {!loading && data.length > 0 && (
          <ResponsiveContainer width="100%" height={Math.max(180, data.length * 40)}>
            <BarChart data={data} layout="vertical">
              <XAxis type="number" tick={{ fontSize: 12 }} />
              <YAxis type="category" dataKey="accountName" tick={{ fontSize: 12 }} width={100} />
              <Tooltip formatter={(value: number) => value.toFixed(2)} />
              <Bar dataKey="balance" name="Balance" radius={[0, 4, 4, 0]}>
                {data.map((item, index) => (
                  <Cell key={index} fill={item.balance >= 0 ? '#3b82f6' : '#ef4444'} />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        )}
      </CardContent>
    </Card>
  )
}
