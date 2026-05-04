import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { Tile } from '@/components/Tile';
import { StatTile } from '@/components/StatTile';
import { CardError } from '../../components/CardError';
import { ChartContainer } from '@/components/ui/chart';
import { useApi } from '../../lib/use-api';
import { usePagination } from '@/hooks/usePagination';
import { Area, AreaChart, CartesianGrid, XAxis, YAxis, Tooltip } from 'recharts';
import { REPORTS_NET_WORTH_OVER_TIME_URL, reportMetaBySlug, type NetWorthSnapshotRowDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportTableCard } from './ReportTableCard';
import { ReportLocalFilterBar } from './ReportLocalFilterBar';
import { useReportsFilters } from './useReportsFilters';

const MONTHS = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];

function monthLabel(row: NetWorthSnapshotRowDto): string {
  return `${MONTHS[row.month - 1]} ${row.year}`;
}

function formatDelta(value: number, symbol: string): string {
  if (value === 0) return `— ${symbol} 0.00`;
  const sign = value > 0 ? '↑' : '↓';
  return `${sign} ${symbol} ${Math.abs(value).toFixed(2)}`;
}

export function NetWorthOverTime() {
  const { filters, toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<NetWorthSnapshotRowDto[]>(REPORTS_NET_WORTH_OVER_TIME_URL(qs));
  const { paginatedItems, currentPage, totalPages, next, prev } = usePagination(data ?? [], 6);

  const first = data?.[0];
  const last  = data?.[data.length - 1];
  const symbol = last?.currencySymbol ?? '€';

  const chartData = (data ?? []).map((row) => ({
    name: monthLabel(row),
    netWorth: row.netWorth,
    assets: row.assets,
    liabilities: row.liabilities,
  }));

  return (
    <div className="space-y-6">
      <ReportHeader title="Net Worth Over Time" description={reportMetaBySlug('net-worth-over-time')?.description} filters={filters} />
      <ReportLocalFilterBar />
      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Net Worth Over Time" onRetry={refetch} />}
      {data && data.length === 0 && (
        <p className="text-sm text-muted-foreground">No data for this period.</p>
      )}
      {data && data.length > 0 && last && first && (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Tile>
              <StatTile
                label="Net Worth"
                value={<Numeric className={`text-xl ${last.netWorth >= 0 ? 'text-success' : 'text-destructive'}`}>{symbol} {last.netWorth.toFixed(2)}</Numeric>}
              />
              <p className={`mt-1 text-xs ${(last.netWorth - first.netWorth) > 0 ? 'text-success' : (last.netWorth - first.netWorth) < 0 ? 'text-destructive' : 'text-muted-foreground'}`}>
                {formatDelta(last.netWorth - first.netWorth, symbol)} vs period start
              </p>
            </Tile>
            <Tile>
              <StatTile
                label="Total Assets"
                value={<Numeric className="text-xl">{symbol} {last.assets.toFixed(2)}</Numeric>}
              />
              <p className={`mt-1 text-xs ${(last.assets - first.assets) > 0 ? 'text-success' : (last.assets - first.assets) < 0 ? 'text-destructive' : 'text-muted-foreground'}`}>
                {formatDelta(last.assets - first.assets, symbol)} vs period start
              </p>
            </Tile>
            <Tile>
              <StatTile
                label="Total Liabilities"
                value={<Numeric className={`text-xl ${last.liabilities > 0 ? 'text-destructive' : ''}`}>{symbol} {last.liabilities.toFixed(2)}</Numeric>}
              />
              <p className={`mt-1 text-xs ${(last.liabilities - first.liabilities) < 0 ? 'text-success' : (last.liabilities - first.liabilities) > 0 ? 'text-destructive' : 'text-muted-foreground'}`}>
                {formatDelta(first.liabilities - last.liabilities, symbol)} vs period start
              </p>
            </Tile>
          </div>

          <ChartContainer
            config={{
              netWorth: { label: 'Net Worth', color: 'var(--primary)' },
            }}
            className="h-[220px] w-full"
          >
            <AreaChart data={chartData} margin={{ top: 8, right: 0, left: 0, bottom: 0 }}>
              <defs>
                <linearGradient id="grad-nwot" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="var(--primary)" stopOpacity={0.18} />
                  <stop offset="95%" stopColor="var(--primary)" stopOpacity={0} />
                </linearGradient>
              </defs>
              <CartesianGrid vertical={false} stroke="var(--border)" />
              <XAxis dataKey="name" tick={{ fontSize: 11, fill: 'var(--muted-foreground)' }} tickLine={false} axisLine={false} />
              <YAxis tick={{ fontSize: 11, fill: 'var(--muted-foreground)' }} tickLine={false} axisLine={false} tickFormatter={(v) => `${(v / 1000).toFixed(0)}k`} />
              <Tooltip
                contentStyle={{ background: 'var(--background)', border: '1px solid var(--border)', borderRadius: '8px', fontSize: 12 }}
                formatter={(value: unknown) => [`${symbol} ${(value as number).toFixed(2)}`, 'Net Worth']}
              />
              <Area type="monotone" dataKey="netWorth" stroke="var(--primary)" strokeWidth={2} fill="url(#grad-nwot)" dot={false} />
            </AreaChart>
          </ChartContainer>

          <ReportTableCard
            slug="net-worth-over-time"
            queryString={qs}
            pagination={totalPages > 1 ? { currentPage, totalPages, onNext: next, onPrev: prev } : undefined}
          >
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Month</TableHead>
                  <TableHead className="text-right">Assets</TableHead>
                  <TableHead className="text-right">Liabilities</TableHead>
                  <TableHead className="text-right">Net Worth</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {paginatedItems.map((row) => (
                  <TableRow key={`${row.year}-${row.month}`}>
                    <TableCell>{monthLabel(row)}</TableCell>
                    <TableCell className="text-right">
                      <Numeric>{row.currencySymbol} {row.assets.toFixed(2)}</Numeric>
                    </TableCell>
                    <TableCell className="text-right">
                      <Numeric>{row.currencySymbol} {row.liabilities.toFixed(2)}</Numeric>
                    </TableCell>
                    <TableCell className="text-right">
                      <Numeric className={row.netWorth >= 0 ? 'text-success' : 'text-destructive'}>
                        {row.currencySymbol} {row.netWorth.toFixed(2)}
                      </Numeric>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </ReportTableCard>
        </>
      )}
    </div>
  );
}
