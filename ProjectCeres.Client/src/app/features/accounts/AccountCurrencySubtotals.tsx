import { Card, CardContent } from '@/components/ui/card';
import type { AccountListItemDto } from './accounts-api';

type Props = {
  rows: AccountListItemDto[];
};

type CurrencyTotal = {
  code: string;
  symbol: string;
  net: number;
};

export function AccountCurrencySubtotals({ rows }: Props) {
  const totals = computeNetPerCurrency(rows);
  if (totals.length < 2) return null;

  return (
    <Card>
      <CardContent className="py-3">
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
          {totals.map((t, idx) => (
            <span key={t.code} className="flex items-center gap-3">
              {idx > 0 ? <span className="text-muted-foreground">·</span> : null}
              <span>
                <span className="text-muted-foreground mr-2">{t.code}</span>
                <span className="tabular-nums font-medium">{formatNet(t)}</span>
              </span>
            </span>
          ))}
        </div>
      </CardContent>
    </Card>
  );
}

function computeNetPerCurrency(rows: AccountListItemDto[]): CurrencyTotal[] {
  const map = new Map<string, CurrencyTotal>();
  for (const r of rows) {
    const cur = map.get(r.currencyCode) ?? { code: r.currencyCode, symbol: r.currencySymbol, net: 0 };
    const sign = r.accountTypeName === 'Liability' ? -1 : 1;
    cur.net += sign * r.balance;
    map.set(r.currencyCode, cur);
  }
  return Array.from(map.values()).sort((a, b) => a.code.localeCompare(b.code));
}

function formatNet(t: CurrencyTotal): string {
  const formatted = new Intl.NumberFormat(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(Math.abs(t.net));
  const sign = t.net < 0 ? '-' : '';
  return `${sign}${t.symbol}${formatted}`;
}
